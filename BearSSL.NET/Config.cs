using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BearSSL
{
    public static class Config
    {
        public static IReadOnlyDictionary<string, int> Values { get; } = Get();

        public static bool Is64Bit { get; } = Values["BR_64"] == 1;
        public static bool HasNativeAesSupport { get; } = Values["BR_AES_X86NI"] == 1;
        public static bool IsBigEndianUnaligned { get; } = Values["BR_BE_UNALIGNED"] == 1;
        public static bool HasInt128 { get; } = Values["BR_INT128"] == 1;
        public static bool IsLittleEndianUnaligned { get; } = Values["BR_LE_UNALIGNED"] == 1;
        public static int MaxElipticCurveKeySize { get; } = Values["BR_MAX_EC_SIZE"];
        public static int MaxRsaKeySize { get; } = Values["BR_MAX_RSA_SIZE"];
        public static int MaxRsaFactor { get; } = Values["BR_MAX_RSA_FACTOR"];
        public static bool UseSse2 { get; } = Values["BR_SSE2"] == 1;

        private static unsafe IReadOnlyDictionary<string, int> Get()
        {
            var configArray = NativeCalls.br_get_config();
            var valuesCount = DetermineConfigLength(configArray);
            var configValues = new Span<br_config_option>(configArray, valuesCount);

            var values = new Dictionary<string, int>(valuesCount);
            for (var i = 0; i < valuesCount; i++)
            {
                var name = Marshal.PtrToStringAnsi(configValues[i].Name);
                var value = configValues[i].Value;
                values.Add(name, value);
            }

            return values;
        }
        
        private static unsafe int DetermineConfigLength(void* configArray)
        {
            for (var i = 1; ; i++)
            {
                var nextValue = new Span<br_config_option>(configArray, i);
                if (nextValue[i - 1].Name == IntPtr.Zero)
                {
                    return i - 1;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private readonly struct br_config_option
        {
            public readonly IntPtr Name;
            public readonly int Value;
        }
    }
}
