namespace p2pcopy
{
    internal enum DataSizeUnit
    {
        Bytes = 1,
        KiloBytes = 1024,
        MegaBytes = 1024 * 1024,
        GigaBytes = 1024 * 1024 * 1024
    };

    internal static class SizeConverter
    {
        internal static string ConvertToSizeString(long size)
        {
            DataSizeUnit totalSizeUnit = SizeConverter.GetSuitableUnit(size);
            return string.Format("{0:#0.##} {1}", SizeConverter.ConvertToSize(
                size, totalSizeUnit), SizeConverter.GetUnitString(totalSizeUnit));
        }

        internal static float ConvertToSize(long size, DataSizeUnit unit)
        {
            return (float)size / (float)unit;
        }

        static string GetUnitString(DataSizeUnit unit)
        {
            switch (unit)
            {
                case DataSizeUnit.Bytes: return "bytes";
                case DataSizeUnit.KiloBytes: return "KB";
                case DataSizeUnit.MegaBytes: return "MB";
                case DataSizeUnit.GigaBytes: return "GB";
            }
            return string.Empty;
        }

        static DataSizeUnit GetSuitableUnit(long size)
        {
            if (size >= 0 && size < (long)DataSizeUnit.KiloBytes)
                return DataSizeUnit.Bytes;
            else if (size >= (long)DataSizeUnit.KiloBytes && size <= (long)DataSizeUnit.MegaBytes)
                return DataSizeUnit.KiloBytes;
            else if (size >= (long)DataSizeUnit.MegaBytes && size <= (long)DataSizeUnit.GigaBytes)
                return DataSizeUnit.MegaBytes;
            else
                return DataSizeUnit.GigaBytes;
        }
    }
}
