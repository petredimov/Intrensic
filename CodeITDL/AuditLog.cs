//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace CodeITDL
{
    using System;
    using System.Collections.Generic;
    
    public partial class AuditLog
    {
        public int Id { get; set; }
        public System.DateTime CreatedOn { get; set; }
        public int CreatedBy { get; set; }
        public string Action { get; set; }
        public string ObjectId { get; set; }
        public string OldObject { get; set; }
        public string NewObject { get; set; }
        public string CustomerId { get; set; }
    }
}