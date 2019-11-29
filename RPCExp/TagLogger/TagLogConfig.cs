﻿using RPCExp.Common;
using RPCExp.TagLogger.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace RPCExp.TagLogger
{
    public class TagLogConfig
    {
        private decimal lastVal = 0;


        private long lastTime = 0;

        public TagAbstract Tag { get; private set; }

        public TagLogInfo TagLogInfo { get; set; }

        /// <summary>
        /// Макс процент от шкалы
        /// </summary>
        public decimal Hyst { get; set; } = 0.10M;

        /// <summary>
        /// Максимальный период в секундах
        /// </summary>
        public int PeriodMaxSec { get; set; } = 600;

        /// <summary>
        /// Минимальный период в секундах
        /// </summary>
        public int PeriodMinSec { get; set; } = 1;

        static TagsGroup TagsLogTagGroup = new TagsGroup(new BasicPeriodSource())
        {
            Name = "TagsLogTagGroup",
            Description = "Tags group to periodicly check alarms",
            Min = 20 * 10_000_000,
        };

        public TagLogConfig(TagAbstract tag)
        {
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
            
            if (tag.Groups.ContainsKey(TagsLogTagGroup.Name))
                tag.Groups.AddByName(TagsLogTagGroup);
        }

        public TagLogData NeedToArcive
        {
            get
            {
                if ((Tag == default) || Tag.Quality < TagQuality.GOOD)
                    return null;

                var now = DateTime.Now.Ticks;
                if(lastTime + PeriodMinSec * 10_000_000 >= now)
                    return null;

#pragma warning disable CA1305 // Укажите IFormatProvider
                var val = (decimal)Convert.ChangeType(Tag?.Value ?? 0, typeof(decimal));
#pragma warning restore CA1305 // Укажите IFormatProvider

                if (((lastTime + PeriodMaxSec * 10_000_000) <= now) || 
                    (Math.Abs(lastVal - val) >= Hyst))
                {
                    lastVal = val;
                    lastTime = now;
                    return new TagLogData 
                    {
                        TagLogInfo = TagLogInfo,
                        TagLogInfoId = TagLogInfo.Id,
                        TimeStamp = lastTime,
                        Value = val,
                    };
                }

                return null;
            }
        }


    }
}
