namespace RecordVersionCompare
{
    public static class StringExtensions
    {
        public static string[] SplitTwo(this string value)
        {
            return value.Trim().Split(new[] { ' ' }, 2);
        }
    }
}
