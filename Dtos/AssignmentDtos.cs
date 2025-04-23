// Dtos/AssignmentDtos.cs
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
    }

    public class UpdateAssignmentDto : CreateAssignmentDto // Can inherit or be separate
    { }

    public class AssignmentBasicDto // For lists
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? DueDate { get; set; }
        public int? MaxPoints { get; set; }
        // Add submission status for current user if needed
        public string? SubmissionStatus { get; set; } // e.g., "Not Submitted", "Submitted", "Late", "Graded"
    }

     public class AssignmentDetailsDto : AssignmentBasicDto // For detail view
    {
        public string? Instructions { get; set; }
        public int CreatedById { get; set; }
        public string CreatedByUsername { get; set; } = string.Empty;
        public int ClassroomId { get; set; }
    }
}

// Dtos/SubmissionDtos.cs
namespace WebCodeWork.Dtos
{
    public class SubmittedFileDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
        // Avoid sending back stored path/name unless necessary for download URLs
    }

    public class SubmissionDto // For viewing a specific submission
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
    }

     public class SubmissionSummaryDto // For lists shown to teachers
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentUsername { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public bool IsLate { get; set; }
        public int? Grade { get; set; }
        public bool HasFiles { get; set; } // Indicate if files exist without fetching all details
    }

    public class GradeSubmissionDto
    {
         // Use nullable types to allow partial updates (only grade or only feedback)
        public int? Grade { get; set; }
        public string? Feedback { get; set; }
    }
}