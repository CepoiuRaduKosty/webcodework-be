// Models/SubmittedFile.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class SubmittedFile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssignmentSubmissionId { get; set; } // FK to AssignmentSubmission

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty; // Original file name

        [Required]
        [MaxLength(255)]
        public string StoredFileName { get; set; } = string.Empty; // Unique name for storage (e.g., GUID)

        [Required]
        [MaxLength(1024)] // Adjust length as needed
        public string FilePath { get; set; } = string.Empty; // Path relative to a base storage location

        [MaxLength(100)]
        public string? ContentType { get; set; } // e.g., "application/pdf", "image/jpeg"

        public long FileSize { get; set; } // Size in bytes

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property
        [ForeignKey(nameof(AssignmentSubmissionId))]
        public virtual AssignmentSubmission AssignmentSubmission { get; set; } = null!;
    }
}