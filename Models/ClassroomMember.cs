// Models/ClassroomMember.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebCodeWork.Enums; // Make sure to import the enum

namespace WebCodeWork.Models
{
    // This entity represents the many-to-many relationship between User and Classroom
    // and stores the role of the user within that specific classroom.
    public class ClassroomMember
    {
        // Composite Primary Key defined in DbContext

        [Required]
        public int UserId { get; set; } // Foreign key to User

        [Required]
        public int ClassroomId { get; set; } // Foreign key to Classroom

        [Required]
        public ClassroomRole Role { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!; // Mark as required reference type

        [ForeignKey(nameof(ClassroomId))]
        public virtual Classroom Classroom { get; set; } = null!; // Mark as required reference type
    }
}