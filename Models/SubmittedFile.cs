
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class SubmittedFile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssignmentSubmissionId { get; set; } 

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty; 

        [Required]
        [MaxLength(255)]
        public string StoredFileName { get; set; } = string.Empty; 

        [Required]
        [MaxLength(1024)] 
        public string FilePath { get; set; } = string.Empty; 

        [MaxLength(100)]
        public string? ContentType { get; set; } 

        public long FileSize { get; set; } 

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        
        [ForeignKey(nameof(AssignmentSubmissionId))]
        public virtual AssignmentSubmission AssignmentSubmission { get; set; } = null!;
    }
}