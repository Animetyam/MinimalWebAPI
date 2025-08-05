using System.ComponentModel.DataAnnotations;
namespace SWA
{
    public class ValueEntry
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public List<CsvModel> Values {get; set;} = new List<CsvModel>();
    }
}