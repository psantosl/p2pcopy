namespace p2pcopy
{
    internal static class SizeConverter
    {
        internal enum EnumUnitSize
        {
            sizeBytes = 1,
            sizeKiloBytes = 1024,
            sizeMegaBytes = 1024 * 1024,
            sizeGygaBytes = 1024 * 1024 * 1024
        };

        internal static string ConvertToSizeString(long size)
        {
            SizeConverter.EnumUnitSize totalSizeUnit = SizeConverter.GetSuitableUnit(size);
            return string.Format("{0:#0.##} {1}", SizeConverter.ConvertToSize(
                size, totalSizeUnit), SizeConverter.GetUnitString(totalSizeUnit));
        }

        internal static float ConvertToSize(long size, EnumUnitSize unit)
        {
            return (float)size / (float)unit;
        }

        static string GetUnitString(EnumUnitSize unit)
        {
            switch (unit)
            {
                case EnumUnitSize.sizeBytes: return "bytes";
                case EnumUnitSize.sizeKiloBytes: return "KB";
                case EnumUnitSize.sizeMegaBytes: return "MB";
                case EnumUnitSize.sizeGygaBytes: return "GB";
            }
            return string.Empty;
        }

        static EnumUnitSize GetSuitableUnit(long size)
        {
            if (size >= 0 && size < (long)EnumUnitSize.sizeKiloBytes)
                return EnumUnitSize.sizeBytes;
            else if (size >= (long)EnumUnitSize.sizeKiloBytes && size <= (long)EnumUnitSize.sizeMegaBytes)
                return EnumUnitSize.sizeKiloBytes;
            else if (size >= (long)EnumUnitSize.sizeMegaBytes && size <= (long)EnumUnitSize.sizeGygaBytes)
                return EnumUnitSize.sizeMegaBytes;
            else
                return EnumUnitSize.sizeGygaBytes;
        }
    }
}
