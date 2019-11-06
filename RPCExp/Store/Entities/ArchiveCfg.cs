﻿using System;

namespace RPCExp.Store.Entities
{
    public class ArchiveCfg: INameDescription, ICopyFrom, IIdentity
    {
        public int Id { get; set; }
        
        public string Name { get; set; }

        public string Description { get; set; }

        public TagCfg Tag { get; set; }

        public int TagCfgId { get; set; }

        public int PeriodMaxSec { get; set; }

        public int PeriodMinSec { get; set; }

        public decimal HystProc { get; set; }

        public void CopyFrom(object original)
        {
            var src = (ArchiveCfg)original;

            Name = src.Name;
            Description = src.Description;
            Tag = src.Tag;
            PeriodMaxSec = src.PeriodMaxSec;
            PeriodMinSec = src.PeriodMinSec;
            HystProc = src.HystProc;

        }
    }
}