using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class Assignment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ClassroomId { get; set; } // FK to Classroom

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Instructions { get; set; } // Can be longer text, potentially HTML/Markdown later

        [Required]
        public int CreatedById { get; set; } // FK to User (Teacher/Owner who created it)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DueDate { get; set; } // Optional deadline
        public int? MaxPoints { get; set; } // Optional max points/grade

        // Navigation Properties
        [ForeignKey(nameof(ClassroomId))]
        public virtual Classroom Classroom { get; set; } = null!;

        [ForeignKey(nameof(CreatedById))]
        public virtual User CreatedBy { get; set; } = null!;

        public virtual ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();

        [Required] // Make IsCodeAssignment required
        public bool IsCodeAssignment { get; set; } = false; // Default to false

        // Add navigation property for test cases
        public virtual ICollection<TestCase> TestCases { get; set; } = new List<TestCase>();
    }
}