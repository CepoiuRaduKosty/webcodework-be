
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class TestCase
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssignmentId { get; set; } 

        
        [Required]
        [MaxLength(255)]
        public string InputFileName { get; set; } = string.Empty; 

        [Required]
        [MaxLength(255)]
        public string InputStoredFileName { get; set; } = string.Empty; 

        [Required]
        [MaxLength(1024)]
        public string InputFilePath { get; set; } = string.Empty; 

        
        [Required]
        [MaxLength(255)]
        public string ExpectedOutputFileName { get; set; } = string.Empty; 

        [Required]
        [MaxLength(255)]
        public string ExpectedOutputStoredFileName { get; set; } = string.Empty; 

        [Required]
        [MaxLength(1024)]
        public string ExpectedOutputFilePath { get; set; } = string.Empty; 

        [Required]
        [Range(0, 1000)] 
        public int Points { get; set; }

        [Required]
        [Range(100, 99999999)] 
        public int MaxExecutionTimeMs { get; set; } = 2000; 

        [Required]
        [Range(32, 99999999)]  
        public int MaxRamMB { get; set; } = 128; 

        
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public int AddedById { get; set; } 

        [Required]
        public bool IsPrivate { get; set; } = false;

        
        [ForeignKey(nameof(AssignmentId))]
        public virtual Assignment Assignment { get; set; } = null!;

        [ForeignKey(nameof(AddedById))]
        public virtual User AddedBy { get; set; } = null!;
    }
}