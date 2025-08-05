namespace SWA
{
    public class Median
    {
        public static double CalculateMedian(List<double> numbers)
        {
            if (numbers == null || numbers.Count == 0)
                throw new InvalidOperationException("Empty collection");

            var sorted = numbers.OrderBy(n => n).ToList();

            if (sorted.Count % 2 == 0)
                return (sorted[sorted.Count/2 - 1] + sorted[sorted.Count/2]) / 2.0;
            else
                return sorted[sorted.Count/2];
        }
    }
}