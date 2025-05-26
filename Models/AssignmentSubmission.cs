using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class AssignmentSubmission
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssignmentId { get; set; } // FK to Assignment

        [Required]
        public int StudentId { get; set; } // FK to User (Student who submitted)

        public DateTime? SubmittedAt { get; set; } // Nullable: Timestamp when student marked as 'Done'
        public bool IsLate { get; set; } = false; // Determined at submission time based on DueDate

        // Grading related fields (nullable)
        public int? Grade { get; set; } // Grade given by teacher/owner
        public string? Feedback { get; set; } // Feedback from teacher/owner
        public DateTime? GradedAt { get; set; }
        public int? GradedById { get; set; } // FK to User (who graded it)

        public int? LastEvaluationPointsObtained { get; set; }
        public int? LastEvaluationTotalPossiblePoints { get; set; }
        public DateTime? LastEvaluatedAt { get; set; }
        public string? LastEvaluationOverallStatus { get; set; }

        public string? LastEvaluationDetailsJson { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(AssignmentId))]
        public virtual Assignment Assignment { get; set; } = null!;

        [ForeignKey(nameof(StudentId))]
        public virtual User Student { get; set; } = null!;

        [ForeignKey(nameof(GradedById))]
        public virtual User? GradedBy { get; set; } // Grader might not always be set

        public virtual ICollection<SubmittedFile> SubmittedFiles { get; set; } = new List<SubmittedFile>();
    }
}