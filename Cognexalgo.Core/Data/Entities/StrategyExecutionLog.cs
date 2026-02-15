using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cognexalgo.Core.Data.Entities
{
    public class StrategyExecutionLog
    {
        [Key]
        public int Id { get; set; }
        
        public int StrategyId { get; set; }
        
        public DateTime ExecutedAt { get; set; }
        
        [MaxLength(50)]
        public string Status { get; set; } // "SUCCESS", "FAILED", "PENDING"
        
        [Column(TypeName = "text")]
        public string ExecutionDetails { get; set; } // JSON with leg results
        
        public int StrategyVersion { get; set; } // Which version was executed
        
        // Foreign key
        // [ForeignKey(nameof(StrategyId))]
        // public HybridStrategyEntity Strategy { get; set; }
    }
}
