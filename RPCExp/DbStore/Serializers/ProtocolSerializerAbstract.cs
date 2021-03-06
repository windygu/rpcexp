﻿using RPCExp.Common;
using RPCExp.DbStore.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RPCExp.DbStore.Serializers
{

    /// <summary>
    /// Класс преобразует сущности из БД в объекты программы и обратно.
    /// </summary>
    internal abstract class ProtocolSerializerAbstract
    {
        public ProtocolSerializerAbstract()
        {
        }

        public abstract string ClassName { get; }

        public Common.Store Store { get; }

        public DeviceAbstract UnpackDevice(DeviceCfg config, Common.Store store)
        {
            DeviceAbstract device = UnpackDeviceSpecific(config.Custom);

            device.Name = config.Name;
            device.Description = config.Description;
            device.BadCommPeriod = config.BadCommWaitPeriod;
            device.UpdateInActiveTags = config.InActiveUpdate;
            device.UpdateInActiveTagsPeriod = config.InActiveUpdatePeriod;

            device.ConnectionSource = store.ConnectionsSources.Values.FirstOrDefault(c => c.Name == config.ConnectionSourceCfg.Name);

            return device;
        }

        public DeviceCfg PackDevice(DeviceAbstract device, StoreContext context)
        {
            var config = context.Devices.GetOrCreate(d => d.Name == device.Name);

            config.ClassName = this.ClassName;
            config.Name = device.Name;
            config.Description = device.Description;
            config.BadCommWaitPeriod = device.BadCommPeriod;
            config.InActiveUpdate = device.UpdateInActiveTags;
            config.InActiveUpdatePeriod = device.UpdateInActiveTagsPeriod;

            config.ConnectionSourceCfg = context.Connections.GetOrCreate(c => c.Name == device.ConnectionSource.Name);

            config.Custom = PackDeviceSpecific(device);

            foreach (var tag in device.Tags.Values)
            {
                var tagCfg = PackTag(tag, context);

                var storedTemplate = context.Templates.GetOrCreate(t => t.Id == tag.TemplateId);
                storedTemplate.Id = tag.TemplateId; //если он создался заново

                var dev2Templ = config.DeviceToTemplates.FirstOrDefault(d2t => d2t.Template.Id == tag.TemplateId);

                if (dev2Templ == default)
                {
                    dev2Templ = context.DeviceToTemplates.GetOrCreate(d2t => d2t.TemplateId == tag.TemplateId && d2t.DeviceId == config.Id);
                    dev2Templ.Device = config;
                    dev2Templ.Template = storedTemplate;
                    config.DeviceToTemplates.Add(dev2Templ);
                }

                dev2Templ.Template.Tags.Add(tagCfg);

            }
            return config;
        }

        public TagAbstract UnpackTag(TagCfg config)
        {
            var t = UnpackTagSpecific(config.Custom);

            t.Name = config.Name;
            t.DisplayName = config.DisplayName;
            t.Description = config.Description;
            t.Format = config.Format;
            t.Access = config.Access;
            t.ValueType = config.ValueType;

            return t;
        }

        public TagCfg PackTag(TagAbstract tag, StoreContext context)
        {
            var config = new TagCfg
            {
                ClassName = this.ClassName,
                Name = tag.Name,
                DisplayName = tag.DisplayName,
                Description = tag.Description,
                Format = tag.Format,
                Access = tag.Access,
                ValueType = tag.ValueType,
                Custom = PackTagSpecific(tag),
                TagsToTagsGroups = new List<TagsToTagsGroups>(),
            };

            foreach (var tagsGroup in tag.Groups.Values)
            {
                var storedGroup = context.TagsGroups.GetOrCreate(tg => tg.Name == tagsGroup.Name);
                storedGroup.Name = tagsGroup.Name;
                storedGroup.Min = tagsGroup.Min;
                storedGroup.Description = tagsGroup.Description;

                var ttg = config.TagsToTagsGroups.FirstOrDefault(o => o.TagsGroupCfg.Name == storedGroup.Name);
                if (ttg == default)
                {
                    ttg = context.TagsToTagsGroups.GetOrCreate(o => o.TagsGroupCfg.Name == storedGroup.Name && o.TagCfg.Id == config.Id);
                    ttg.TagCfg = config;
                    ttg.TagsGroupCfg = storedGroup;
                }
                config.TagsToTagsGroups.Add(ttg);
            }

            return config;
        }

        protected abstract string PackDeviceSpecific(DeviceAbstract device);

        protected abstract DeviceAbstract UnpackDeviceSpecific(string custom);

        protected abstract TagAbstract UnpackTagSpecific(string custom);

        protected abstract string PackTagSpecific(TagAbstract tag);


    }
}