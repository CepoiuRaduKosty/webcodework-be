// Dtos/TestCaseDtos.cs
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class TestCaseListDto // For lists, used by GET /api/assignments/{assignmentId}/testcases
    {
        public int Id { get; set; }
        public string InputFileName { get; set; } = string.Empty;
        public string ExpectedOutputFileName { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; }
        public string AddedByUsername { get; set; } = string.Empty;
        public int Points { get; set; } // Add Points here too if you want it in the list view
    }

    // Used as response for POST /api/assignments/{assignmentId}/testcases
    public class TestCaseDetailDto
    {
        public int Id { get; set; }
        public string InputFileName { get; set; } = string.Empty;
        public string ExpectedOutputFileName { get; set; } = string.Empty;
        public int Points { get; set; } // <<-- Include Points
        public DateTime AddedAt { get; set; }
        public string AddedByUsername { get; set; } = string.Empty;
        // Add other details if needed, like file paths (but generally not for client DTOs)
    }
}