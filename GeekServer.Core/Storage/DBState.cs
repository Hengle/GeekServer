﻿using System.Threading;
using System.ComponentModel;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace Geek.Server
{
    public abstract class DBState : InnerDBState
    {
        ///<summary>mongodbId=actorId<para/>
        ///需要注意mongodb不区分_id/id/Id/ID
        ///</summary>
        public long Id { get; set; }
    }

    //https://github.com/Fody/PropertyChanged/wiki/EventInvokerSelectionInjection
    public abstract class InnerDBState : BaseState, INotifyPropertyChanged
    {
        /// <summary>PropertyChanged需要，勿动，保持为null即可</summary>
        public event PropertyChangedEventHandler PropertyChanged;
        public virtual void OnPropertyChanged(string propertyName)
        {
            _stateChanged = true;
            var evt = PropertyChanged;
            if (evt != null)
                evt.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        List<System.Reflection.PropertyInfo> bsList = new List<System.Reflection.PropertyInfo>();
        public InnerDBState()
        {
            //待优化 改成生成dll时注入
            var arr = GetType().GetProperties(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | (System.Reflection.BindingFlags.SetProperty & System.Reflection.BindingFlags.SetProperty));

            foreach (var p in arr)
            {
                if (p.PropertyType.IsSubclassOf(typeof(BaseState)))
                    bsList.Add(p);
            }
        }

        public override bool IsChanged
        {
            get
            {
                if (_stateChanged)
                    return _stateChanged;
                foreach (var bs in bsList)
                {
                    var s = bs.GetValue(this) as BaseState;
                    if (s != null && s.IsChanged)
                        return true;
                }
                return false;
            }
        }

        public override void ClearChanges()
        {
            //base.ClearChanges();
            _stateChanged = false;
            foreach (var bs in bsList)
            {
                var s = bs.GetValue(this) as BaseState;
                if (s != null)
                    s.ClearChanges();
            }
        }
    }


    [BsonIgnoreExtraElements(true, Inherited = true)]//忽略代码删除的字段[数据库多余的字段]
    public abstract class BaseState
    {
        protected bool _stateChanged;
        /// <summary>需要在actor线程内不调用才安全</summary>
        public virtual bool IsChanged => _stateChanged;
        /// <summary>需要在actor线程内不调用才安全</summary>
        public virtual void ClearChanges()
        {
            _stateChanged = false;
        }

        #region thread safe save
        volatile int changeVersion;
        volatile int savedVersion;
        volatile int tosaveVersion;

        ///<summary>更新改变</summary>
        public void UpdateChangeVersion()
        {
            if (IsChanged)
                Interlocked.Increment(ref changeVersion);
            ClearChanges();
        }

        ///<summary>存数据库前先await入队保存要存数据库的change版本</summary>
        public void ReadyToSaveToDB()
        {
            Interlocked.Exchange(ref tosaveVersion, changeVersion);
        }

        ///<summary>保存完后修改已保存版本号</summary>
        public void SavedToDB()
        {
            Interlocked.Exchange(ref savedVersion, tosaveVersion);
        }

        ///<summary>相对数据库是否有改变</summary>
        public bool IsChangedRefDB(bool updateVersion = false)
        {
            var ret = changeVersion > savedVersion;
            if (!ret && updateVersion)
                return IsChanged;
            return ret;
        }
        #endregion
    }
}
