﻿using Microsoft.EntityFrameworkCore;
using RPCExp.Common;
using RPCExp.TagLogger.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RPCExp.TagLogger
{
    /// <summary>
    /// Tag logging service class
    /// After starting it watching 
    /// </summary>
    public class TagLogService : ServiceAbstract
    {
        //TODO: Учесть в этом классе "запущенность" сервера при добавлении/удалении групп в опрашиваемые теги.
        private const int baseCapacityOfTmpList = 32; // Начальная емкость промежуточного хранилища

        private const int minWaitTimeMs = 50; // Минимальное время ожидания, мсек

        /// <summary>
        /// Period for maintain db. Maintain will start when save new messages into db AND this period is elapsed. 
        /// </summary>
        public TimeSpan MinMaintainPeriod { get; set; } = TimeSpan.FromSeconds(10);

        private DateTime nextMaintain = DateTime.Now;

        /// <summary>
        /// Period for check conditions of dband or period elapsed.
        /// </summary>
        public TimeSpan CheckPeriod { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Period for saving data into db. Data can be saved faster, if caching buffer is full.
        /// </summary>
        public TimeSpan SavePeriod { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Limit of stored items in DB
        /// </summary>
        public long StoreItemsCount { get; set; } = 10_000_000;

        private long DeltaRecordsCount => 1 + StoreItemsCount * 5 / 100;

        /// <summary>
        /// SQlite db file name
        /// </summary>
        public string FileName { get; set; } = "alarmLog.sqlite3";

        /// <summary>
        /// Configured tags for archiving
        /// </summary>
        public List<TagLogConfig> Configs { get; } = new List<TagLogConfig>();

        private async Task InnitDB(CancellationToken cancellationToken)
        {
            var context = new TagLogContext(FileName);

            var storedInfo = await context.TagLogInfo.ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var cfg in Configs)
            {
                var storedTagLogInfo = context.TagLogInfo.FirstOrDefault(e =>
                    e.FacilityAccessName == cfg.TagLogInfo.FacilityAccessName &&
                    e.DeviceName == cfg.TagLogInfo.DeviceName &&
                    e.TagName == cfg.TagLogInfo.TagName);

                if (storedTagLogInfo == default)
                {
                    storedTagLogInfo = new TagLogInfo
                    {
                        FacilityAccessName = cfg.TagLogInfo.FacilityAccessName,
                        DeviceName = cfg.TagLogInfo.DeviceName,
                        TagName = cfg.TagLogInfo.TagName,
                    };
                    context.TagLogInfo.Add(storedTagLogInfo);
                    storedInfo.Add(storedTagLogInfo);
                }
                cfg.TagLogInfo = storedTagLogInfo;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            context.Dispose();
        }

        private async Task SaveAsync(Queue<TagLogData> cache, CancellationToken cancellationToken)
        {
            if ((cache?.Count ?? 0) == 0)
                return;

            var context = new TagLogContext(FileName);

            /* // Код как оно должно работать
            context.TagLogData.AddRange(cache);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            */

            // ########## Начало костыля
            // TODO: при новых версиях EF Core (> 3.0.1) пробовать убрать этот костыль
            const int maxItemsInInsert = 128;
            while (cache.Count > 0)
            {
                var len = cache.Count > maxItemsInInsert ? maxItemsInInsert : cache.Count;

                var sql = "INSERT INTO TagLogData (\"TimeStamp\", \"TagLogInfoId\", \"Value\") VALUES ";
                for (var i = 0; i < len; i++)
                {
                    var item = cache.Dequeue();
                    sql += $"({item.TimeStamp}, {item.TagLogInfoId}, {item.Value})" + ",";
                }

                sql = sql.Trim().Trim(',') + ';';

                await context.Database.ExecuteSqlRawAsync(sql).ConfigureAwait(false);
            }
            // ########## Конец костыля

            if (nextMaintain < DateTime.Now)
            {
                nextMaintain = DateTime.Now + MinMaintainPeriod;

                var count = await context.TagLogData.LongCountAsync(cancellationToken).ConfigureAwait(false);

                if (count > StoreItemsCount)
                {
                    var countToRemove = count - StoreItemsCount + DeltaRecordsCount;
                    int ctr = countToRemove < 0 ? 0 :
                            countToRemove > int.MaxValue ? int.MaxValue :
                            (int)countToRemove;

                    var itemsToRemove = context.TagLogData.Take(ctr);

                    context.TagLogData.RemoveRange(itemsToRemove);

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await context.Database.ExecuteSqlRawAsync("VACUUM ;").ConfigureAwait(false);

                    nextMaintain = DateTime.Now + 4 * MinMaintainPeriod; // после такого можно чуть подольше не проверять:)
                }
            }

            context.Dispose();
        }


        /// <inheritdoc/>
        protected override async Task ServiceTaskAsync(CancellationToken cancellationToken)
        {
            // Старт (Инициализация контекста БД алармов)
            await InnitDB(cancellationToken).ConfigureAwait(false);

            var cache = new List<TagLogData>(baseCapacityOfTmpList);

            var tNextSave = DateTime.Now + SavePeriod;

            // Главный цикл (проверка алармов и запись)
            while (!cancellationToken.IsCancellationRequested)
            {
                var tNextCheck = DateTime.Now + CheckPeriod;

                foreach (var cfg in Configs)
                {
                    try
                    {
                        var archiveData = cfg.NeedToArcive;
                        if (archiveData != default)
                        {
                            cache.Add(new TagLogData
                            {
                                //TagLogInfo = cfg.TagLogInfo,
                                TagLogInfoId = cfg.TagLogInfo.Id,
                                TimeStamp = archiveData.TimeStamp,
                                Value = archiveData.Value,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError(GetType().Name + ":" + ex.InnerMessage());
                    }
                }//for

                try
                {
                    if (cache.Count > 0)
                    {
                        if ((tNextSave <= DateTime.Now) || (cache.Count >= (baseCapacityOfTmpList * 4 / 5))) // 80% заполненности - чтобы избежать разрастания памяти
                        {
                            tNextSave = DateTime.Now + SavePeriod;
                            var newCache = new Queue<TagLogData>(cache);
                            _ = Task.Run(async () =>
                            {
                                await SaveAsync(newCache, cancellationToken).ConfigureAwait(false);
                            });
                            cache.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(GetType().Name + ":" + ex.InnerMessage());
                }

                int tSleep = tNextCheck > DateTime.Now ? (int)(tNextCheck - DateTime.Now).TotalMilliseconds : minWaitTimeMs;

                await Task.Delay(tSleep).ConfigureAwait(false);
            }//while
        }

        /// <summary>
        /// Получение информации о хранящихся в архиве переменных.
        /// Id этих параметров используются в запросе архивных данных.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<TagLogInfo> GetInfos()
        {
            return from cfg in Configs
                   select cfg.TagLogInfo;
        }

        /// <summary>
        /// Получить архивные данные.
        /// </summary>
        /// <param name="ids">Идентификаторы параметров</param>
        /// <param name="tBegin">Время начала для выборки</param>
        /// <param name="tEnd">время окончания выборки</param>
        /// <returns></returns>
        public async Task<IEnumerable<TagLogData>> GetData(TagLogFilter filter)
        {
            var context = new TagLogContext(FileName);

            var query = from a in context.TagLogData
                        select a;

            var offset = 0;
            var count = 200;

#pragma warning disable CA1307 // Укажите StringComparison
            if (filter != default)
            {
                if (filter.TBegin != long.MinValue)
                    query = query.Where(a => a.TimeStamp >= filter.TBegin);

                if (filter.TEnd != long.MaxValue)
                    query = query.Where(a => a.TimeStamp <= filter.TEnd);

                if (filter.InfoIds != default)
                    query = query.Where(a => filter.InfoIds.Contains(a.TagLogInfo.Id));
                
                if (filter.FacilityAccessName != default)
                    query = query.Where(a => a.TagLogInfo.FacilityAccessName == filter.FacilityAccessName);

                if (filter.DeviceName != default)
                    query = query.Where(a => a.TagLogInfo.DeviceName == filter.DeviceName);

                if (filter.TagNames != default)
                    query = query.Where(a => filter.TagNames.Contains(a.TagLogInfo.TagName));

                if (filter.Count != 0)
                {
                    offset = filter.Offset;
                    count = filter.Count;
                }
            }
#pragma warning restore CA1307 // Укажите StringComparison

            var result = await query.Skip(offset).Take(count).ToListAsync().ConfigureAwait(false);

            context.Dispose();
            return result;
        }
    }
    /// <summary>
    /// Messages filter
    /// Every member of this class is optional.
    /// </summary>
    public class TagLogFilter
    {
        /// <summary>
        /// Time of the begin selection.
        /// </summary>
        public long TBegin { get; set; } = long.MinValue;


        /// <summary>
        /// Time of the end selection.
        /// </summary>
        public long TEnd { get; set; } = long.MaxValue;

        /// <summary>
        /// List of ids of concrete tags.
        /// </summary>
        public IEnumerable<int> InfoIds { get; set; }
        
        /// <summary>
        /// Select facility related archive tags.
        /// </summary>
        public string FacilityAccessName { get; set; }

        /// <summary>
        /// Select archives related to device with this name
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// Select archives data by tags names
        /// </summary>
        public IEnumerable<string> TagNames { get; set; }

        /// <summary>
        /// Part of pagination. Sets limit offset for the resulting query.
        /// </summary>
        public int Offset { get; set; } = 0;

        /// <summary>
        /// Part of pagination. Sets limit count for the resulting query.
        /// </summary>
        public int Count { get; set; } = 0;
    }
}
