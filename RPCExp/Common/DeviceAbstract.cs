﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using RPCExp.Connections;

namespace RPCExp.Common
{
    public abstract class DeviceAbstract : ServiceAbstract, INameDescription
    {
        public virtual string Name { get ; set ; }

        public string Description { get; set; }
        
        public long BadCommWaitPeriod { get; set; } = 10 * 10_000_000;

        public bool UpdateInActiveTags { get; set; } = true;

        public long UpdateInActiveTagsPeriod { get; set; } = 20 * 10_000_000;

        public IDictionary<string, TagsGroup> Groups { get; } = new Dictionary<string, TagsGroup>();

        public IDictionary<string, TagAbstract> Tags { get; } = new Dictionary<string, TagAbstract>();
        
        public ConnectionSourceAbstract ConnectionSource { get; set; }

        protected ICollection<TagAbstract> NeedToUpdate(out long nextTime, bool force = false)
        {
            var nowTick = DateTime.Now.Ticks;
            var afterTick = nowTick + TimeSpan.FromSeconds(1).Ticks;
            nextTime = nowTick + BadCommWaitPeriod;
            List<TagAbstract> retTags = new List<TagAbstract>();
            
            foreach (var tag in Tags.Values)
            {
                if ((!tag.IsActive) && (!UpdateInActiveTags) && (!force))
                    continue;

                long period = (tag.Quality == TagQuality.GOOD) ?
                    tag.Period :
                    BadCommWaitPeriod;

                if ((!tag.IsActive) && UpdateInActiveTags)
                    period = UpdateInActiveTagsPeriod;

                long tagNextTick = tag.Last + period;

                if (tagNextTick > afterTick)
                {
                    if (nextTime > tagNextTick)
                        nextTime = tagNextTick;
                    if (!force)
                        continue;
                }
                else
                {
                    tagNextTick = nowTick + period;
                    if (nextTime > tagNextTick)
                        nextTime = tagNextTick;
                }
                retTags.Add(tag);
            }

            return retTags;
        }

        public virtual IDictionary< string, IEnumerable<string>> GetTagsGroups()
        {
            var r = new Dictionary<string, IEnumerable<string>>(Tags.Count);
            foreach (var t in Tags.Values)
                r.Add(t.Name, t.Groups.Values.Select(s=>s.Name));
            return r;
        }

        protected override async Task ServiceTaskAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested) {
                (long nextTime, bool IOUpdateOk) = await IOUpdate(cancellationToken).ConfigureAwait(false);
                // TODO: Вынести сюда общую логику определения необходимости. Абстрактным должен быть метод получающий данные для коллекции тегов.


                // Ожидание следующего цикла
                long waitTime = nextTime - DateTime.Now.Ticks;
                waitTime = waitTime < 0 ? 0 : waitTime;
                waitTime = waitTime > 10_000 ? waitTime : 10_000; // 10_000 = 1 миллисекунда
                waitTime = waitTime > 50_000_000 ? waitTime / 2 : waitTime;
                waitTime = waitTime < 50_000_000 ? waitTime : 50_000_000;// 100_000_000 = 10 сек
                
                await Task.Delay((int)(waitTime / 10_000)).ConfigureAwait(false);
            }
        }


        /// <summary>
        /// Обновление тегов
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>long - next time for update, bool - update was successfull</returns>
        public abstract Task<(long, bool)> IOUpdate(CancellationToken cancellationToken);


        public virtual IEnumerable<object> GetGroupInfos(string groupName)
        {
            return from t in Tags.Values
                   where t.Groups.ContainsKey(groupName)
                   select t.GetInfo();
        }

        /// <summary>
        /// Получает значения набора переменных
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        /// <example>
        /// { "jsonrpc": "2.0", "method": "f1$Plc1.GetGroupValues", "params": ["usts2"], "id": "159"}
        /// </example>
        public virtual IEnumerable<TagData> GetGroupValues(string groupName)
        {

            if (!Groups.ContainsKey(groupName))
                return null;

            List<TagData> datas = new List<TagData>();

            Groups[groupName].Tick();

            return from t in Tags.Values
                     where t.Groups.ContainsKey(groupName)
                     select new TagData(t);
        }

        /// <summary>
        /// Получить значения нескольких тэгов
        /// </summary>
        /// <param name="tagNames">имена тэгов</param>
        /// <returns>значения</returns>
        /// <example>
        /// { "jsonrpc": "2.0", "method": "f1$Plc1.GetTagsValues", "params": [["DATA_95"]], "id": "159"}
        /// </example>
        public virtual ICollection<TagData> GetTagsValues(IEnumerable<string> tagNames)
        {
            List<TagData> datas = new List<TagData>();

            if (tagNames is null)
                return datas;

            foreach (string tagName in tagNames)
            {
                TagData td = null;

                if (Tags.ContainsKey(tagName))
                {
                    var tag = Tags[tagName];
                    td = new TagData(tag);
                }
                datas.Add(td);
            }

            return datas;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tagsValues"></param>
        /// <returns></returns>
        /// <example>
        /// { "jsonrpc": "2.0", "method": "f1$Plc1.Write", "params": [{"UST_112":"-5"}], "id": "159"}
        /// </example>
        public abstract Task<int> Write(IDictionary<string, object> tagsValues);
    }

}
