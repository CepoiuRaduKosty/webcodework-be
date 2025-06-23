
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebCodeWork.Enums; 

namespace WebCodeWork.Models
{
    
    
    public class ClassroomMember
    {
        

        [Required]
        public int UserId { get; set; } 

        [Required]
        public int ClassroomId { get; set; } 

        [Required]
        public ClassroomRole Role { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        
        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!; 

        [ForeignKey(nameof(ClassroomId))]
        public virtual Classroom Classroom { get; set; } = null!; 
    }
}