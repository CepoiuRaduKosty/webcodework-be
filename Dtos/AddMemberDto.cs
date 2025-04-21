using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class AddMemberDto
    {
        [Required]
        public int UserId { get; set; } // The ID of the user to add
    }
}