
namespace WebCodeWork.Dtos
{
    
    public class TeacherSubmissionViewDto
    {
        
        public int StudentId { get; set; }
        public string StudentUsername { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; }

        
        public int? SubmissionId { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public bool IsLate { get; set; } 
        public int? Grade { get; set; }
        public bool HasFiles { get; set; } 
        public string Status { get; set; } = "Not Submitted"; 
        public int? LastEvaluationPointsObtained { get; set; }
        public int? LastEvaluationTotalPossiblePoints { get; set; }
        public DateTime? LastEvaluatedAt { get; set; }
        public string? LastEvaluationOverallStatus { get; set; }

        public string? LastEvaluationDetailsJson { get; set; }
    }
}