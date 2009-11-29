﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VimCore;
using System.Windows.Input;
using VimCoreTest.Utils;

namespace VimCoreTest
{
    [TestClass]
    public class CountCaptureTest
    {
        private CountResult.Complete Process(string input)
        {
            var res = CountCapture.Process(InputUtil.CharToKeyInput(input[0]));
            foreach (var cur in input.Skip(1))
            {
                Assert.IsTrue(res.IsNeedMore);
                var i = InputUtil.CharToKeyInput(cur);
                res = res.AsNeedMore().item.Invoke(i);
            }

            Assert.IsTrue(res.IsComplete);
            return (CountResult.Complete)res;
        }

        [TestMethod]
        public void Simple1()
        {
            var res = Process("A");

            Assert.AreEqual(1, res.Item1);
            Assert.AreEqual(Key.A, res.Item2.Key);
            Assert.AreEqual(ModifierKeys.Shift, res.Item2.ModifierKeys);
        }


        [TestMethod]
        public void Simple2()
        {
            var res = Process("1A");
            Assert.AreEqual(1, res.Item1);
            Assert.AreEqual(Key.A, res.Item2.Key);
        }

        [TestMethod]
        public void Simple3()
        {
            var res = Process("23B");
            Assert.AreEqual(23, res.Item1);
            Assert.AreEqual(Key.B, res.Item2.Key);
        }





    }
}