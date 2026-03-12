using System;

namespace PrimafitERP.Api.DTOs
{
    
    public class SegAccountResponseDto
    {
        public Guid Id { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool AllowJournal { get; set; }
    }
}