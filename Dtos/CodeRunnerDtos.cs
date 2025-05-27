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
        public bool MaximumMemoryException { get; set; }
        public string? TestCaseName { get; set; }
        public bool? IsPrivate { get; set; }
    }

    public class CodeRunnerEvaluateResponse
    {
        public string OverallStatus { get; set; } = "Error";
        public bool CompilationSuccess { get; set; }
        public string? CompilerOutput { get; set; }
        public List<CodeRunnerTestCaseResult> Results { get; set; } = new List<CodeRunnerTestCaseResult>();
    }

    public class EvaluationResultSignalRD
    {
        public int SubmissionId { get; set; }
        public string EvaluatedLanguage { get; set; } = string.Empty;
        public string OverallStatus { get; set; } = "Error";
        public bool CompilationSuccess { get; set; }
        public string? CompilerOutput { get; set; }
        public List<CodeRunnerTestCaseResult> Results { get; set; } = new List<CodeRunnerTestCaseResult>();

        public int? PointsObtained { get; set; }
        public int? TotalPossiblePoints { get; set; }
    }
    
    public static class EvaluationStatus
    {
        public const string Accepted = "ACCEPTED";
        public const string WrongAnswer = "WRONG_ANSWER";
        public const string CompileError = "COMPILE_ERROR";
        public const string RuntimeError = "RUNTIME_ERROR";
        public const string TimeLimitExceeded = "TIME_LIMIT_EXCEEDED";
        public const string MemoryLimitExceeded = "MEMORY_LIMIT_EXCEEDED";
        public const string FileError = "FILE_ERROR";
        public const string LanguageNotSupported = "LANGUAGE_NOT_SUPPORTED";
        public const string InternalError = "INTERNAL_ERROR";
    }
}