﻿using Concentus.Common.CPlusPlus;
using Concentus.Common;
using Concentus.Silk.Enums;
using Concentus.Silk.Structs;
using System.Diagnostics;

namespace Concentus.Silk
{
    public static class regularize_correlations
    {
        /* Add noise to matrix diagonal */
        public static void silk_regularize_correlations_FIX(
            Pointer<int> XX,                                    /* I/O  Correlation matrices                                                        */
            Pointer<int> xx,                                    /* I/O  Correlation values                                                          */
            int noise,                                  /* I    Noise to add                                                                */
            int D                                       /* I    Dimension of XX                                                             */
        )
        {
            int i;
            for (i = 0; i < D; i++)
            {
                Inlines.matrix_adr(XX, i, i, D)[0] = Inlines.silk_ADD32(Inlines.matrix_ptr(XX, i, i, D), noise);
            }
            xx[0] += noise;
        }
    }
}
