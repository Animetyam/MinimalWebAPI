using CsvHelper.Configuration.Attributes;
namespace SWA
{
    public class CsvModel
    {
        [Ignore]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public double ExecutionTime { get; set; }
        public double Value { get; set; }
    }
}
