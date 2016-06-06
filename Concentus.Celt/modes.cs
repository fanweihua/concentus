﻿using Concentus.Celt.Enums;
using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Common.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Concentus.Celt
{
    public static class modes
    {
        public static readonly CELTMode mode48000_960_120 = new CELTMode
        {
            Fs = 48000,
            overlap = 120,
            nbEBands = 21,
            effEBands = 21,
            preemph = new int[] { 27853, 0, 4096, 8192 },
            eBands = Tables.eband5ms.GetPointer(),
            maxLM = 3,
            nbShortMdcts = 8,
            shortMdctSize = 120,
            nbAllocVectors = 11,
            allocVectors = Tables.band_allocation.GetPointer(),
            logN = Tables.logN400.GetPointer(),
            window = Tables.window120.GetPointer(),
            mdct = new mdct_lookup()
            {
                n = 1920,
                maxshift = 3,
                kfft = new kiss_fft_state[]
                {
                    Tables.fft_state48000_960_0,
                    Tables.fft_state48000_960_1,
                    Tables.fft_state48000_960_2,
                    Tables.fft_state48000_960_3,
                },
                trig = Tables.mdct_twiddles960.GetPointer()
            },
            cache = new PulseCache()
            {
                size = 392,
                index = Tables.cache_index50.GetPointer(),
                bits = Tables.cache_bits50.GetPointer(),
                caps = Tables.cache_caps50.GetPointer(),
            }
        };

        private static readonly CELTMode[] static_mode_list = new CELTMode[] {
            mode48000_960_120,
        };

        public static CELTMode opus_custom_mode_create(int Fs, int frame_size, BoxedValue<int> error)
        {
            int i;

            for (i = 0; i < CeltConstants.TOTAL_MODES; i++)
            {
                int j;
                for (j = 0; j < 4; j++)
                {
                    if (Fs == static_mode_list[i].Fs &&
                          (frame_size << j) == static_mode_list[i].shortMdctSize * static_mode_list[i].nbShortMdcts)
                    {
                        if (error != null)
                            error.Val = OpusError.OPUS_OK;
                        return static_mode_list[i];
                    }
                }
            }

            if (error != null)
                error.Val = OpusError.OPUS_BAD_ARG;

            return null;
        }
    }
}
