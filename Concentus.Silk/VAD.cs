﻿using Concentus.Common.CPlusPlus;
using Concentus.Common;
using Concentus.Silk.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus.Silk
{
    /// <summary>
    /// Voice Activity Detection module for silk codec
    /// </summary>
    public static class VAD
    {
        /// <summary>
        /// Weighting factors for tilt measure
        /// </summary>
        private static readonly int[] tiltWeights = { 30000, 6000, -12000, -12000 };

        /// <summary>
        /// Initialization of the Silk VAD
        /// </summary>
        /// <param name="psSilk_VAD">O  Pointer to Silk VAD state. Cannot be nullptr</param>
        /// <returns>0 if success</returns>
        public static int silk_VAD_Init(silk_VAD_state psSilk_VAD)
        {
            int b, ret = 0;

            /* reset state memory */
            psSilk_VAD.Reset();

            /* init noise levels */
            /* Initialize array with approx pink noise levels (psd proportional to inverse of frequency) */
            for (b = 0; b < SilkConstants.VAD_N_BANDS; b++)
            {
                psSilk_VAD.NoiseLevelBias[b] = Inlines.silk_max_32(Inlines.silk_DIV32_16(SilkConstants.VAD_NOISE_LEVELS_BIAS, Inlines.CHOP16(b + 1)), 1);
            }

            /* Initialize state */
            for (b = 0; b < SilkConstants.VAD_N_BANDS; b++)
            {
                psSilk_VAD.NL[b] = Inlines.silk_MUL(100, psSilk_VAD.NoiseLevelBias[b]);
                psSilk_VAD.inv_NL[b] = Inlines.silk_DIV32(int.MaxValue, psSilk_VAD.NL[b]);
            }

            psSilk_VAD.counter = 15;

            /* init smoothed energy-to-noise ratio*/
            for (b = 0; b < SilkConstants.VAD_N_BANDS; b++)
            {
                psSilk_VAD.NrgRatioSmth_Q8[b] = 100 * 256;       /* 100 * 256 -. 20 dB SNR */
            }

            return (ret);
        }



        /// <summary>
        /// Get the speech activity level in Q8
        /// </summary>
        /// <param name="psEncC">I/O  Encoder state</param>
        /// <param name="pIn">I    PCM input</param>
        /// <returns>0 if success</returns>
        public static int silk_VAD_GetSA_Q8_c(
            silk_encoder_state psEncC,
            Pointer<short> pIn)
        {
            int SA_Q15, pSNR_dB_Q7, input_tilt;
            int decimated_framelength1, decimated_framelength2;
            int decimated_framelength;
            int dec_subframe_length, dec_subframe_offset, SNR_Q7, i, b, s;
            int sumSquared = 0, smooth_coef_Q16;
            short HPstateTmp;
            Pointer<short> X;
            int[] Xnrg = new int[SilkConstants.VAD_N_BANDS];
            int[] NrgToNoiseRatio_Q8 = new int[SilkConstants.VAD_N_BANDS];
            int speech_nrg, x_tmp;
            int[] X_offset = new int[SilkConstants.VAD_N_BANDS];
            int ret = 0;
            silk_VAD_state psSilk_VAD = psEncC.sVAD;

            /* Safety checks */
            Debug.Assert(SilkConstants.VAD_N_BANDS == 4);
            Debug.Assert(SilkConstants.MAX_FRAME_LENGTH >= psEncC.frame_length);
            Debug.Assert(psEncC.frame_length <= 512);
            Debug.Assert(psEncC.frame_length == 8 * Inlines.silk_RSHIFT(psEncC.frame_length, 3));

            /***********************/
            /* Filter and Decimate */
            /***********************/
            decimated_framelength1 = Inlines.silk_RSHIFT(psEncC.frame_length, 1);
            decimated_framelength2 = Inlines.silk_RSHIFT(psEncC.frame_length, 2);
            decimated_framelength = Inlines.silk_RSHIFT(psEncC.frame_length, 3);

            /* Decimate into 4 bands:
               0       L      3L       L              3L                             5L
                       -      --       -              --                             --
                       8       8       2               4                              4

               [0-1 kHz| temp. |1-2 kHz|    2-4 kHz    |            4-8 kHz           |

               They're arranged to allow the minimal ( frame_length / 4 ) extra
               scratch space during the downsampling process */
            X_offset[0] = 0;
            X_offset[1] = decimated_framelength + decimated_framelength2;
            X_offset[2] = X_offset[1] + decimated_framelength;
            X_offset[3] = X_offset[2] + decimated_framelength2;
            X = Pointer.Malloc<short>(X_offset[3] + decimated_framelength1);

            /* 0-8 kHz to 0-4 kHz and 4-8 kHz */
            Filters.silk_ana_filt_bank_1(pIn, psSilk_VAD.AnaState,
                X, X.Point(X_offset[3]), psEncC.frame_length);

            /* 0-4 kHz to 0-2 kHz and 2-4 kHz */
            Filters.silk_ana_filt_bank_1(X, psSilk_VAD.AnaState1,
                X, X.Point(X_offset[2]), decimated_framelength1);

            /* 0-2 kHz to 0-1 kHz and 1-2 kHz */
            Filters.silk_ana_filt_bank_1(X, psSilk_VAD.AnaState2,
                X, X.Point(X_offset[1]), decimated_framelength2);

            /*********************************************/
            /* HP filter on lowest band (differentiator) */
            /*********************************************/
            X[decimated_framelength - 1] = Inlines.CHOP16(Inlines.silk_RSHIFT(X[decimated_framelength - 1], 1));
            HPstateTmp = X[decimated_framelength - 1];

            for (i = decimated_framelength - 1; i > 0; i--)
            {
                X[i - 1] = Inlines.CHOP16(Inlines.silk_RSHIFT(X[i - 1], 1));
                X[i] -= X[i - 1];
            }

            X[0] -= psSilk_VAD.HPstate;
            psSilk_VAD.HPstate = HPstateTmp;

            /*************************************/
            /* Calculate the energy in each band */
            /*************************************/
            for (b = 0; b < SilkConstants.VAD_N_BANDS; b++)
            {
                /* Find the decimated framelength in the non-uniformly divided bands */
                decimated_framelength = Inlines.silk_RSHIFT(psEncC.frame_length, Inlines.silk_min_int(SilkConstants.VAD_N_BANDS - b, SilkConstants.VAD_N_BANDS - 1));

                /* Split length into subframe lengths */
                dec_subframe_length = Inlines.silk_RSHIFT(decimated_framelength, SilkConstants.VAD_INTERNAL_SUBFRAMES_LOG2);
                dec_subframe_offset = 0;

                /* Compute energy per sub-frame */
                /* initialize with summed energy of last subframe */
                Xnrg[b] = psSilk_VAD.XnrgSubfr[b];
                for (s = 0; s < SilkConstants.VAD_INTERNAL_SUBFRAMES; s++)
                {
                    sumSquared = 0;

                    for (i = 0; i < dec_subframe_length; i++)
                    {
                        /* The energy will be less than dec_subframe_length * ( silk_int16_MIN / 8 ) ^ 2.            */
                        /* Therefore we can accumulate with no risk of overflow (unless dec_subframe_length > 128)  */
                        x_tmp = Inlines.silk_RSHIFT(
                            X[X_offset[b] + i + dec_subframe_offset], 3);
                        sumSquared = Inlines.silk_SMLABB(sumSquared, x_tmp, x_tmp);

                        /* Safety check */
                        Debug.Assert(sumSquared >= 0);
                    }

                    /* Add/saturate summed energy of current subframe */
                    if (s < SilkConstants.VAD_INTERNAL_SUBFRAMES - 1)
                    {
                        Xnrg[b] = Inlines.silk_ADD_POS_SAT32(Xnrg[b], sumSquared);
                    }
                    else
                    {
                        /* Look-ahead subframe */
                        Xnrg[b] = Inlines.silk_ADD_POS_SAT32(Xnrg[b], Inlines.silk_RSHIFT(sumSquared, 1));
                    }

                    dec_subframe_offset += dec_subframe_length;
                }

                psSilk_VAD.XnrgSubfr[b] = sumSquared;
            }

            /********************/
            /* Noise estimation */
            /********************/
            silk_VAD_GetNoiseLevels(new Pointer<int>(Xnrg), psSilk_VAD);

            /***********************************************/
            /* Signal-plus-noise to noise ratio estimation */
            /***********************************************/
            sumSquared = 0;
            input_tilt = 0;
            for (b = 0; b < SilkConstants.VAD_N_BANDS; b++)
            {
                speech_nrg = Xnrg[b] - psSilk_VAD.NL[b];
                if (speech_nrg > 0)
                {
                    /* Divide, with sufficient resolution */
                    if ((Xnrg[b] & 0xFF800000) == 0)
                    {
                        NrgToNoiseRatio_Q8[b] = Inlines.silk_DIV32(Inlines.silk_LSHIFT(Xnrg[b], 8), psSilk_VAD.NL[b] + 1);
                    }
                    else {
                        NrgToNoiseRatio_Q8[b] = Inlines.silk_DIV32(Xnrg[b], Inlines.silk_RSHIFT(psSilk_VAD.NL[b], 8) + 1);
                    }

                    /* Convert to log domain */
                    SNR_Q7 = Inlines.silk_lin2log(NrgToNoiseRatio_Q8[b]) - 8 * 128;

                    /* Sum-of-squares */
                    sumSquared = Inlines.silk_SMLABB(sumSquared, SNR_Q7, SNR_Q7);          /* Q14 */

                    /* Tilt measure */
                    if (speech_nrg < ((int)1 << 20))
                    {
                        /* Scale down SNR value for small subband speech energies */
                        SNR_Q7 = Inlines.silk_SMULWB(Inlines.silk_LSHIFT(Inlines.silk_SQRT_APPROX(speech_nrg), 6), SNR_Q7);
                    }
                    input_tilt = Inlines.silk_SMLAWB(input_tilt, tiltWeights[b], SNR_Q7);
                }
                else
                {
                    NrgToNoiseRatio_Q8[b] = 256;
                }
            }

            /* Mean-of-squares */
            sumSquared = Inlines.silk_DIV32_16(sumSquared, SilkConstants.VAD_N_BANDS); /* Q14 */

            /* Root-mean-square approximation, scale to dBs, and write to output pointer */
            pSNR_dB_Q7 = (short)(3 * Inlines.silk_SQRT_APPROX(sumSquared)); /* Q7 */

            /*********************************/
            /* Speech Probability Estimation */
            /*********************************/
            SA_Q15 = Sigmoid.silk_sigm_Q15(Inlines.silk_SMULWB(SilkConstants.VAD_SNR_FACTOR_Q16, pSNR_dB_Q7) - SilkConstants.VAD_NEGATIVE_OFFSET_Q5);

            /**************************/
            /* Frequency Tilt Measure */
            /**************************/
            psEncC.input_tilt_Q15 = Inlines.silk_LSHIFT(Sigmoid.silk_sigm_Q15(input_tilt) - 16384, 1);

            /**************************************************/
            /* Scale the sigmoid output based on power levels */
            /**************************************************/
            speech_nrg = 0;
            for (b = 0; b < SilkConstants.VAD_N_BANDS; b++)
            {
                /* Accumulate signal-without-noise energies, higher frequency bands have more weight */
                speech_nrg += (b + 1) * Inlines.silk_RSHIFT(Xnrg[b] - psSilk_VAD.NL[b], 4);
            }

            /* Power scaling */
            if (speech_nrg <= 0)
            {
                SA_Q15 = Inlines.silk_RSHIFT(SA_Q15, 1);
            }
            else if (speech_nrg < 32768)
            {
                if (psEncC.frame_length == 10 * psEncC.fs_kHz)
                {
                    speech_nrg = Inlines.silk_LSHIFT_SAT32(speech_nrg, 16);
                }
                else
                {
                    speech_nrg = Inlines.silk_LSHIFT_SAT32(speech_nrg, 15);
                }

                /* square-root */
                speech_nrg = Inlines.silk_SQRT_APPROX(speech_nrg);
                SA_Q15 = Inlines.silk_SMULWB(32768 + speech_nrg, SA_Q15);
            }

            /* Copy the resulting speech activity in Q8 */
            psEncC.speech_activity_Q8 = Inlines.silk_min_int(Inlines.silk_RSHIFT(SA_Q15, 7), byte.MaxValue);

            /***********************************/
            /* Energy Level and SNR estimation */
            /***********************************/
            /* Smoothing coefficient */
            smooth_coef_Q16 = Inlines.silk_SMULWB(SilkConstants.VAD_SNR_SMOOTH_COEF_Q18, Inlines.silk_SMULWB((int)SA_Q15, SA_Q15));

            if (psEncC.frame_length == 10 * psEncC.fs_kHz)
            {
                smooth_coef_Q16 >>= 1;
            }

            for (b = 0; b < SilkConstants.VAD_N_BANDS; b++)
            {
                /* compute smoothed energy-to-noise ratio per band */
                psSilk_VAD.NrgRatioSmth_Q8[b] = Inlines.silk_SMLAWB(psSilk_VAD.NrgRatioSmth_Q8[b],
                  NrgToNoiseRatio_Q8[b] - psSilk_VAD.NrgRatioSmth_Q8[b], smooth_coef_Q16);

                /* signal to noise ratio in dB per band */
                SNR_Q7 = 3 * (Inlines.silk_lin2log(psSilk_VAD.NrgRatioSmth_Q8[b]) - 8 * 128);
                /* quality = sigmoid( 0.25 * ( SNR_dB - 16 ) ); */
                psEncC.input_quality_bands_Q15[b] = Sigmoid.silk_sigm_Q15(Inlines.silk_RSHIFT(SNR_Q7 - 16 * 128, 4));
            }

            return (ret);
        }

        /// <summary>
        /// Noise level estimation
        /// </summary>
        /// <param name="pX">I    subband energies [VAD_N_BANDS]</param>
        /// <param name="psSilk_VAD">I/O  Pointer to Silk VAD state</param>
        public static void silk_VAD_GetNoiseLevels(
            Pointer<int> pX,
            silk_VAD_state psSilk_VAD)
        {
            int k;
            int nl, nrg, inv_nrg;
            int coef, min_coef;

            /* Initially faster smoothing */
            if (psSilk_VAD.counter < 1000)
            { /* 1000 = 20 sec */
                min_coef = Inlines.silk_DIV32_16(short.MaxValue, Inlines.CHOP16(Inlines.silk_RSHIFT(psSilk_VAD.counter, 4) + 1));
            }
            else
            {
                min_coef = 0;
            }

            for (k = 0; k < SilkConstants.VAD_N_BANDS; k++)
            {
                /* Get old noise level estimate for current band */
                nl = psSilk_VAD.NL[k];
                Debug.Assert(nl >= 0);

                /* Add bias */
                nrg = Inlines.silk_ADD_POS_SAT32(pX[k], psSilk_VAD.NoiseLevelBias[k]);
                Debug.Assert(nrg > 0);

                /* Invert energies */
                inv_nrg = Inlines.silk_DIV32(int.MaxValue, nrg);
                Debug.Assert(inv_nrg >= 0);

                /* Less update when subband energy is high */
                if (nrg > Inlines.silk_LSHIFT(nl, 3))
                {
                    coef = SilkConstants.VAD_NOISE_LEVEL_SMOOTH_COEF_Q16 >> 3;
                }
                else if (nrg < nl)
                {
                    coef = SilkConstants.VAD_NOISE_LEVEL_SMOOTH_COEF_Q16;
                }
                else
                {
                    coef = Inlines.silk_SMULWB(Inlines.silk_SMULWW(inv_nrg, nl), SilkConstants.VAD_NOISE_LEVEL_SMOOTH_COEF_Q16 << 1);
                }

                /* Initially faster smoothing */
                coef = Inlines.silk_max_int(coef, min_coef);

                /* Smooth inverse energies */
                psSilk_VAD.inv_NL[k] = Inlines.silk_SMLAWB(psSilk_VAD.inv_NL[k], inv_nrg - psSilk_VAD.inv_NL[k], coef);
                Debug.Assert(psSilk_VAD.inv_NL[k] >= 0);

                /* Compute noise level by inverting again */
                nl = Inlines.silk_DIV32(int.MaxValue, psSilk_VAD.inv_NL[k]);
                Debug.Assert(nl >= 0);

                /* Limit noise levels (guarantee 7 bits of head room) */
                nl = Inlines.silk_min(nl, 0x00FFFFFF);

                /* Store as part of state */
                psSilk_VAD.NL[k] = nl;
            }

            /* Increment frame counter */
            psSilk_VAD.counter++;
        }
    }
}
