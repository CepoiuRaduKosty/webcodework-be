
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class AddTestCaseDto
    {
        
        public IFormFile? InputFile { get; set; }
        public IFormFile? OutputFile { get; set; }

        
         [MaxLength(255)]
        public string? InputFileName { get; set; }

         [MaxLength(255)]
        public string? OutputFileName { get; set; }

        [Required(ErrorMessage = "Points for the test case are required.")]
        [Range(0, 1000, ErrorMessage = "Points must be between 0 and 1000.")] 
        public int Points { get; set; }
        
        [Required(ErrorMessage = "Maximum execution time is required.")]
        [Range(100, 100000, ErrorMessage = "Execution time must be between 100ms and 10000ms (10s).")] 
        public int MaxExecutionTimeMs { get; set; } = 2000; 

        [Required(ErrorMessage = "Maximum RAM limit is required.")]
        [Range(32, 512, ErrorMessage = "RAM limit must be between 32MB and 512MB.")]  
        public int MaxRamMB { get; set; } = 128; 

        [Required(ErrorMessage = "Is Private required")]
        public bool IsPrivate { get; set; } = false;

        
        public ValidationResult? ValidateFilenames(ValidationContext context)
        {
            if (InputFile == null && string.IsNullOrWhiteSpace(InputFileName))
            {
                return new ValidationResult("InputFileName is required when InputFile is not provided.", new[] { nameof(InputFileName) });
            }
            if (OutputFile == null && string.IsNullOrWhiteSpace(OutputFileName))
            {
                return new ValidationResult("OutputFileName is required when OutputFile is not provided.", new[] { nameof(OutputFileName) });
            }
            
            return ValidationResult.Success;
        }

    }
}