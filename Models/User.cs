
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebCodeWork.Models
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(255)]
        public string? ProfilePhotoOriginalName { get; set; }

        [MaxLength(255)]
        public string? ProfilePhotoStoredName { get; set; }

        [MaxLength(1024)]
        public string? ProfilePhotoContentType { get; set; }
        
        [MaxLength(1024)]
        public string? ProfilePhotoPath { get; set; }

        public virtual ICollection<ClassroomMember> ClassroomMemberships { get; set; } = new List<ClassroomMember>();
    }
}