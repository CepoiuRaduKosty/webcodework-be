// Dtos/TestCaseDtos.cs
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    // For listing test cases (doesn't expose storage details)
    public class TestCaseListDto
    {
        public int Id { get; set; }
        public string InputFileName { get; set; } = string.Empty;
        public string ExpectedOutputFileName { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; }
        public string AddedByUsername { get; set; } = string.Empty;
    }

    // Maybe needed for response after creation (includes ID)
    public class TestCaseDetailDto : TestCaseListDto
    {
       // Could add file sizes etc. if needed
    }

    // --- No specific Create DTO needed if using multipart/form-data directly ---
}