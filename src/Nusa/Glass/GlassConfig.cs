namespace Nusa.Glass
{
    /// <summary>
    /// "Liquid Glass" (suyuq shisha) effektining BARCHA raqamli qiymatlari shu yerda.
    /// O'zgartirib, qayta yig'sangiz kifoya — boshqa fayllarga tegish shart emas.
    /// Shaffoflik qiymatlari 0..1 oralig'ida (0 = ko'rinmas, 1 = to'liq).
    /// </summary>
    public static class GlassConfig
    {
        // ---------------- 1-bosqich: fon ----------------
        /// <summary>Akril/blur ishlamasa ishlatiladigan bir xil rangli fon shaffofligi.</summary>
        public const double FallbackOpacity = 0.55;

        /// <summary>Windows akril (accent) uchun tizim tusi: ARGB, juda yengil bo'lishi kerak.</summary>
        public const uint AccentTintDark = 0x18000000;   // qora ≈ 9%
        public const uint AccentTintLight = 0x18FFFFFF;  // oq ≈ 9%

        // ---------------- 2-bosqich: shisha qatlamlari ----------------
        /// <summary>Yopiq (kompakt) notch tusi.</summary>
        public const double TintLight = 0.08;   // yorug' rejim: oq 4–12%
        public const double TintDark = 0.15;    // qorong'i rejim: qora 10–20%
        /// <summary>Matn ko'p bo'lgan holat (ochilgan notch, ogohlantirish) uchun tus.</summary>
        public const double TextTint = 0.35;
        /// <summary>Fon juda yorug'/qorong'i bo'lsa tus shu qiymatgacha avtomatik kuchayadi (sinish rejimida).</summary>
        public const double MaxAutoTint = 0.50;

        /// <summary>Qirradagi yaltiroq chiziq.</summary>
        public const double RimThickness = 1.5;
        public const double RimBright = 0.90;   // yorug'lik tushgan tomon (yuqori-chap)
        public const double RimMid = 0.22;
        public const double RimDim = 0.45;      // qarama-qarshi tomon (pastki-o'ng)
        public const double LightAngleDeg = 60; // yorug'lik tushish burchagi (gorizontaldan)

        /// <summary>Ichki yumshoq nur: qirradan ichkariga so'nadi.</summary>
        public const double InnerGlowOpacity = 0.14;   // 10–18%
        public const double InnerGlowDepth = 18;       // px, 15–20
        public const double InnerGlowStroke = 6;       // nur manbai chizig'i qalinligi

        /// <summary>Tepadagi xira oq nur dog'i.</summary>
        public const double TopSpotOpacity = 0.25;
        public const double TopSpotHeightRatio = 0.65; // notch balandligiga nisbatan

        /// <summary>Tashqi soya.</summary>
        public const double ShadowOffsetY = 8;     // 6–10
        public const double ShadowBlur = 22;       // 14–30
        public const double ShadowOpacity = 0.28;  // 15–35%

        // ---------------- 3-bosqich: harakat ----------------
        /// <summary>Prujina: so'nish koeffitsienti (0..1, kichikroq = ko'proq sakraydi) va tebranish soni.</summary>
        public const double SpringDamping = 0.55;
        public const double SpringFrequency = 1.55;
        public const double SpringDuration = 0.45;   // soniya, 0.35–0.5

        /// <summary>Bosilganda kattalashish va yorug'lik to'lqini.</summary>
        public const double PressScale = 1.04;       // 3–5%
        public const double RippleOpacity = 0.35;
        public const double RippleDuration = 0.55;

        /// <summary>Sichqoncha shu masofadan (px) yaqinlashganda qirra yorug'ligi unga buriladi.</summary>
        public const double RimFollowDistance = 260;
        public const double RimFollowStrength = 0.6; // 0 = burilmaydi, 1 = to'liq sichqoncha tomonga

        // ---------------- 4-bosqich: haqiqiy sinish (standart: o'chiq) ----------------
        public const int CaptureMargin = 30;         // notch atrofidan olinadigan qo'shimcha joy, px
        public const double BezelWidth = 20;         // qirradagi sinish tasmasi, px (16–24)
        public const double BezelStrength = 1.25;    // siljish = (1-h)·bezel·kuch
        public const double RefractiveIndex = 1.5;
        public const double ChromaR = 1.00, ChromaG = 1.07, ChromaB = 1.14; // kamalak
        public const double Saturation = 1.7;        // 1.6–1.8
        public const double RefractionBlur = 2;      // px, 0–4
        public const int RefractionFps = 30;
        /// <summary>Ketma-ket shuncha kadr butunlay qora kelsa (DRM yoki xato) — 2-bosqichga qaytiladi.</summary>
        public const int BlackFramesBeforeFallback = 45;
    }
}
