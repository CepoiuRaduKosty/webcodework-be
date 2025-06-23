
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class CreateAssignmentDto
    {
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        public string? Instructions { get; set; }
        public DateTime? DueDate { get; set; }
        public int? MaxPoints { get; set; }

        public bool IsCodeAssignment { get; set; } = false;

    }

    public class UpdateAssignmentDto
    {
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        public string? Instructions { get; set; }
        public DateTime? DueDate { get; set; }
        public int? MaxPoints { get; set; }
    }

    public class AssignmentBasicDto 
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? DueDate { get; set; }
        public int? MaxPoints { get; set; }
        
        public string? SubmissionStatus { get; set; } 
        public bool IsCodeAssignment { get; set; }
    }

    public class AssignmentDetailsDto : AssignmentBasicDto 
    {
        public string? Instructions { get; set; }
        public int CreatedById { get; set; }
        public string CreatedByUsername { get; set; } = string.Empty;
        public int ClassroomId { get; set; }
    }
}


namespace WebCodeWork.Dtos
{
    public class SubmittedFileDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
        
    }

    public class SubmissionDto 
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public int StudentId { get; set; }
        public string StudentUsername { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public bool IsLate { get; set; }
        public int? Grade { get; set; }
        public string? Feedback { get; set; }
        public DateTime? GradedAt { get; set; }
        public int? GradedById { get; set; }
        public string? GradedByUsername { get; set; }
        public List<SubmittedFileDto> SubmittedFiles { get; set; } = new List<SubmittedFileDto>();
        public int? LastEvaluationPointsObtained { get; set; }
        public int? LastEvaluationTotalPossiblePoints { get; set; }
        public DateTime? LastEvaluatedAt { get; set; }
        public string? LastEvaluationOverallStatus { get; set; }

        public string? LastEvaluationDetailsJson { get; set; }
        public string? LastEvaluatedLanguage { get; set; }
    }

    public class SubmissionSummaryDto 
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentUsername { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public bool IsLate { get; set; }
        public int? Grade { get; set; }
        public bool HasFiles { get; set; } 
    }

    public class GradeSubmissionDto
    {
        
        public int? Grade { get; set; }
        public string? Feedback { get; set; }
    }
}