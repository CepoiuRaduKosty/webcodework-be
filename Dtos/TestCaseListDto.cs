
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class TestCaseListDto 
    {
        public int Id { get; set; }
        public string InputFileName { get; set; } = string.Empty;
        public string ExpectedOutputFileName { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; }
        public string AddedByUsername { get; set; } = string.Empty;
        public int Points { get; set; }
        public int MaxExecutionTimeMs { get; set; }
        public int MaxRamMB { get; set; }
        public bool IsPrivate { get; set; }
    }

    
    public class TestCaseDetailDto
    {
        public int Id { get; set; }
        public string InputFileName { get; set; } = string.Empty;
        public string ExpectedOutputFileName { get; set; } = string.Empty;
        public int Points { get; set; }
        public int MaxExecutionTimeMs { get; set; }
        public int MaxRamMB { get; set; }
        public DateTime AddedAt { get; set; }
        public string AddedByUsername { get; set; } = string.Empty;
        public bool IsPrivate { get; set; }
    }

    public class UpdateTestCasePrivacyDto
    {
        [Required(ErrorMessage = "The IsPrivate field is required.")]
        public bool IsPrivate { get; set; }
    }
}