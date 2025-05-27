// Models/TestCase.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class TestCase
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssignmentId { get; set; } // FK to Assignment

        // Input File Info
        [Required]
        [MaxLength(255)]
        public string InputFileName { get; set; } = string.Empty; // Original name

        [Required]
        [MaxLength(255)]
        public string InputStoredFileName { get; set; } = string.Empty; // Stored name (GUID.ext)

        [Required]
        [MaxLength(1024)]
        public string InputFilePath { get; set; } = string.Empty; // Relative storage path

        // Expected Output File Info
        [Required]
        [MaxLength(255)]
        public string ExpectedOutputFileName { get; set; } = string.Empty; // Original name

        [Required]
        [MaxLength(255)]
        public string ExpectedOutputStoredFileName { get; set; } = string.Empty; // Stored name (GUID.ext)

        [Required]
        [MaxLength(1024)]
        public string ExpectedOutputFilePath { get; set; } = string.Empty; // Relative storage path

        [Required]
        [Range(0, 1000)] // Example: Points between 0 and 1000. Adjust as needed.
        public int Points { get; set; }

        [Required]
        [Range(100, 99999999)] // Example: 100ms to 5 seconds
        public int MaxExecutionTimeMs { get; set; } = 2000; // Default to 2 seconds

        [Required]
        [Range(32, 99999999)]  // Example: 32MB to 512MB
        public int MaxRamMB { get; set; } = 128; // Default to 128 MB

        // Audit Info
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public int AddedById { get; set; } // FK to User (who added it)

        [Required]
        public bool IsPrivate { get; set; } = false;

        // Navigation Properties
        [ForeignKey(nameof(AssignmentId))]
        public virtual Assignment Assignment { get; set; } = null!;

        [ForeignKey(nameof(AddedById))]
        public virtual User AddedBy { get; set; } = null!;
    }
}