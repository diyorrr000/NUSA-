using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Nusa.Glass
{
    /// <summary>
    /// "Jele"dek prujinali harakat: maqsaddan ozgina o'tib ketib, tebranib to'xtaydi.
    /// Kam so'nadigan prujinaning sakrash javobi: 1 − e^(−ζωt)·(cos ωd·t + ζω/ωd · sin ωd·t).
    /// EasingMode = EaseIn (standart) bilan ishlating.
    /// </summary>
    public sealed class SpringEase : EasingFunctionBase
    {
        public SpringEase() { EasingMode = EasingMode.EaseIn; }

        protected override double EaseInCore(double t)
        {
            if (t >= 1) return 1;
            double z = GlassConfig.SpringDamping;
            double w = 2 * Math.PI * GlassConfig.SpringFrequency;
            double wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
        }

        protected override Freezable CreateInstanceCore() => new SpringEase();
    }
}
