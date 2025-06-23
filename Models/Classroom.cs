
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace WebCodeWork.Models
{
    public class Classroom
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(255)]
        public string? PhotoOriginalName { get; set; } 

        [MaxLength(255)]
        public string? PhotoStoredName { get; set; } 

        [MaxLength(1024)]
        public string? PhotoContentType { get; set; } 

        [MaxLength(1024)]
        public string? PhotoPath { get; set; } 

        
        public virtual ICollection<ClassroomMember> Members { get; set; } = new List<ClassroomMember>();
    }
}