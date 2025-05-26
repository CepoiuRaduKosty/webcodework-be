// Dtos/TeacherSubmissionViewDto.cs
namespace WebCodeWork.Dtos
{
    // Represents one student's status for a specific assignment in the teacher's view
    public class TeacherSubmissionViewDto
    {
        // Student Info
        public int StudentId { get; set; }
        public string StudentUsername { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; }

        // Submission Info (Nullable if not submitted)
        public int? SubmissionId { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public bool IsLate { get; set; } // Only relevant if SubmittedAt is not null
        public int? Grade { get; set; }
        public bool HasFiles { get; set; } // Indicates if files were uploaded
        public string Status { get; set; } = "Not Submitted"; // Calculated status: Not Submitted, Submitted, Late, Graded
    }
}