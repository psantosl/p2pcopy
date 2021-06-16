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
            DataSizeUnit totalSizeUnit = GetSuitableUnit(size);
            return string.Format("{0:#0.##} {1}", ConvertToSize(
                size, totalSizeUnit), GetUnitString(totalSizeUnit));
        }

        internal static float ConvertToSize(long size, DataSizeUnit unit)
        {
            return (float)size / (float)unit;
        }

        static string GetUnitString(DataSizeUnit unit)
        {
            return unit switch
            {
                DataSizeUnit.Bytes => "bytes",
                DataSizeUnit.KiloBytes => "KB",
                DataSizeUnit.MegaBytes => "MB",
                DataSizeUnit.GigaBytes => "GB",
                _ => string.Empty,
            };
        }

        static DataSizeUnit GetSuitableUnit(long size)
        {
            return size switch
            {
                >= 0 and < (long)DataSizeUnit.KiloBytes => DataSizeUnit.Bytes,
                >= (long)DataSizeUnit.KiloBytes and <= (long)DataSizeUnit.MegaBytes => DataSizeUnit.KiloBytes,
                >= (long)DataSizeUnit.MegaBytes and <= (long)DataSizeUnit.GigaBytes => DataSizeUnit.MegaBytes,
                _ => DataSizeUnit.GigaBytes
            };
        }
    }
}
