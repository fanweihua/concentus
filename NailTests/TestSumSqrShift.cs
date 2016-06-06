﻿using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Concentus.Silk;
using Concentus.Common.CPlusPlus;

namespace NailTests
{
    [TestClass]
    public class TestSumSqrShift
    {
        [TestMethod]
        public void Test_silk_sum_sqr_shift()
        {
            short[] inVectors = { 208, 38, -217, -393, -162, 147, -29, 117, 347, 117, 232, 50, -217, -393, -162, 141, -47, 105, 323, -11463, -17865, -15143, -11990, -8765, -6825, -5582, -4273, -3345, -2769, -2315, -1720, -1242, -946, -769, -696, -277, 141, 38, 220, 92, 226, 92, -174, -344, -126, 183, 383, 553, 656, 747, 820, 487, 117, -144, 32, -95, -338, -108, -229, -429, -506, 3241, 3483, 2387, 2035, 1332, 920, 561, 561, 311, 326, 179, -120, -286, -430, -513, -332, -347, -438, -245, -41, 122, 12, -150, -30, -101, 16, 181, 291, 152, 198, 73, -116, -222, -82, 103, 235, 99, 148, 27, 88, 235, 322, 179, -26, 73, 213, 322, 406, 216, 9, 95, 235, 334, 409, 220, 243, 111, 156, 50, 111, 266, 107, -71, 20, -75, 16, 179, 35, -136, -18, 141, 11, -140, -264, -362, -3750, -4034, -3122, -2642, -1810, -1277, -1069, -778, -400, -392, -434, -150, 107, 54, -67, -3504, -3595, -2695, -2067, -1534, -1398, -959, -619, -517, -313, -302, -320, -339, -370, -400, -188, 43, -44, -177, -286, -377, -188, -218, -82, 111, 232, 341, 409, 232, 258, 122, -67, -184, -60, -124, -22, -97, -252, -94, 78, -18, 11, -36, -139, -422, -92, 115, -219, 63, 444, 113, 293, 260, 77, -240, 48, 459, 352, 671, 616, -14545, -21343, -14026, -13353, -10956, -8485, -7233, -5337, -4028, -3338, -2987, -2283, -1554, -1275, -1115, -827, -182, 395, 177, 3734, 3985, 3008, 2420, 1646, 1269, 1036, 1204, 1287, 1382, };
            BoxedValue<int> energy = new BoxedValue<int>();
            BoxedValue<int> shift = new BoxedValue<int>();

            SumSqrShift.silk_sum_sqr_shift(energy, shift, new Pointer<short>(inVectors), 240);

            Assert.AreEqual(647124903, energy.Val);
            Assert.AreEqual(2, shift.Val);
        }


        [TestMethod]
        public void Test_silk_sum_sqr_shift_2()
        {
            short[] inVectors = { -141, 141, -141, 141, 141, 141, 141, -141, -141, 141, -141, -141, 141, -141, 4018, 141, 141, 141, -141, -141, -141, -141, 141, 141, 141, 141, 141, -141, 141, -141, -141, -141, -141, -141, -141, 141, -141, 141, 141, 141, 141, 141, -141, 141, 141, 141, 141, -141, 141, -141, -141, 141, 141, -141, 141, -141, -141, 141, 141, -141, };
            BoxedValue<int> energy = new BoxedValue<int>();
            BoxedValue<int> shift = new BoxedValue<int>();

            SumSqrShift.silk_sum_sqr_shift(energy, shift, new Pointer<short>(inVectors), 60);

            Assert.AreEqual(17317303, energy.Val);
            Assert.AreEqual(0, shift.Val);
        }
    }
}
