using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class Assignment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ClassroomId { get; set; } 

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Instructions { get; set; } 

        [Required]
        public int CreatedById { get; set; } 

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DueDate { get; set; } 
        public int? MaxPoints { get; set; } 

        
        [ForeignKey(nameof(ClassroomId))]
        public virtual Classroom Classroom { get; set; } = null!;

        [ForeignKey(nameof(CreatedById))]
        public virtual User CreatedBy { get; set; } = null!;

        public virtual ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();

        [Required] 
        public bool IsCodeAssignment { get; set; } = false; 

        
        public virtual ICollection<TestCase> TestCases { get; set; } = new List<TestCase>();
    }
}