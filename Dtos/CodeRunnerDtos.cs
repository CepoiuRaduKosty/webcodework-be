// Dtos/CodeRunnerDtos.cs (in your Main Backend project)
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos // Adjust namespace as needed
{
    // --- Request DTO to call CodeRunnerService ---
    public class CodeRunnerTestCaseInfo
    {
        [Required]
        public string InputFilePath { get; set; } = string.Empty;
        [Required]
        public string ExpectedOutputFilePath { get; set; } = string.Empty;
        public string? TestCaseId { get; set; } // Optional identifier
        [Required]
        public int MaxExecutionTimeMs { get; set; }
        [Required]
        public int MaxRamMB { get; set; }
    }

    public class CodeRunnerEvaluateRequest
    {
        [Required]
        public string Language { get; set; } = string.Empty;
        public string? Version { get; set; }
        [Required]
        public string CodeFilePath { get; set; } = string.Empty;
        [Required]
        [MinLength(1)]
        public List<CodeRunnerTestCaseInfo> TestCases { get; set; } = new List<CodeRunnerTestCaseInfo>();
    }

    // --- Response DTO from CodeRunnerService ---
    public class CodeRunnerTestCaseResult
    {
        public string TestCaseInputPath { get; set; } = string.Empty;
        public string? TestCaseId { get; set; }
        [Required]
        public string Status { get; set; } = "INTERNAL_ERROR"; // Default
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public string? Message { get; set; }
        public long? DurationMs { get; set; }
    }

    public class CodeRunnerEvaluateResponse
    {
        public string OverallStatus { get; set; } = "Error";
        public bool CompilationSuccess { get; set; }
        public string? CompilerOutput { get; set; }
        public List<CodeRunnerTestCaseResult> Results { get; set; } = new List<CodeRunnerTestCaseResult>();
    }
}