

namespace Sitecore.Support.Analytics.Automation
{
    using System.Threading;
    using Diagnostics;
    using Services;
    using Sitecore.Analytics.Configuration;
    using Threading;

    public class AutomationWorker : Sitecore.Analytics.Automation.AutomationWorker
    {
        // Covers the 'alarm' field of the base class. It is safe because its usages are all written (Start and Stop) 
        private AlarmClock alarm;

        private static int activeThreads;

        public override bool Start()
        {
            if (!AnalyticsSettings.Enabled)
            {
                Log.Info("AutomationWorker was not started as Analytics is disabled.", this);
                return false;
            }

            this.alarm = new AlarmClock(this.Interval);
            this.alarm.Ring += delegate { this.WakeupV2(); };

            return true;
        }

        public override void Stop()
        {
            if (this.alarm != null)
            {
                this.alarm.Dispose();
            }
        }

        public new static int ActiveThreads
        {
            get
            {
                return activeThreads;
            }
        }


        internal void WakeupV2()
        {
            for (var i = 0; i < this.ThreadsCount - activeThreads; i++)
            {
                ManagedThreadPool.QueueUserWorkItem(state =>
                {
                    Interlocked.Increment(ref activeThreads);
                    try
                    {
                        this.CleanUpThreadContext();
                        Process();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeThreads);
                    }
                });
            }
        }

        protected virtual void CleanUpThreadContext()
        {
            Sitecore.Context.Items.Clear();
        }
    }
}