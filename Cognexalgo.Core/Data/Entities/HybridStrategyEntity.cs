using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cognexalgo.Core.Data.Entities
{
    public class HybridStrategyEntity
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; }
        
        [Required]
        [Column(TypeName = "text")]
        public string ConfigJson { get; set; } // Serialized HybridStrategyConfig
        
        public bool IsActive { get; set; }
        
        public DateTime CreatedAt { get; set; }
        
        public DateTime? LastModified { get; set; }
        
        public int Version { get; set; } // Incremented on each update
        
        [MaxLength(50)]
        public string? CreatedBy { get; set; }
        
        [MaxLength(50)]
        public string? LastModifiedBy { get; set; }
        
        // Navigation properties
        // public ICollection<StrategyExecutionLog> ExecutionLogs { get; set; }
    }
}
