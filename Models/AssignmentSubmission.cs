using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class AssignmentSubmission
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssignmentId { get; set; } 

        [Required]
        public int StudentId { get; set; } 

        public DateTime? SubmittedAt { get; set; } 
        public bool IsLate { get; set; } = false; 

        
        public int? Grade { get; set; } 
        public string? Feedback { get; set; } 
        public DateTime? GradedAt { get; set; }
        public int? GradedById { get; set; } 

        public int? LastEvaluationPointsObtained { get; set; }
        public int? LastEvaluationTotalPossiblePoints { get; set; }
        public DateTime? LastEvaluatedAt { get; set; }
        public string? LastEvaluationOverallStatus { get; set; }

        public string? LastEvaluationDetailsJson { get; set; }

        public string? LastEvaluatedLanguage { get; set; }

        
        [ForeignKey(nameof(AssignmentId))]
        public virtual Assignment Assignment { get; set; } = null!;

        [ForeignKey(nameof(StudentId))]
        public virtual User Student { get; set; } = null!;

        [ForeignKey(nameof(GradedById))]
        public virtual User? GradedBy { get; set; } 

        public virtual ICollection<SubmittedFile> SubmittedFiles { get; set; } = new List<SubmittedFile>();
    }
}