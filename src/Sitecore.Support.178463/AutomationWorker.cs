

namespace Sitecore.Support.Analytics.Automation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using Sitecore.Analytics;
    using Sitecore.Analytics.Automation;
    using Sitecore.Analytics.Automation.Data;
    using Sitecore.Analytics.Automation.MarketingAutomation;
    using Sitecore.Analytics.Configuration;
    using Sitecore.Analytics.DataAccess;
    using Sitecore.Analytics.Model;
    using Sitecore.Analytics.Tracking;
    using Sitecore.Diagnostics;
    using Sites;

    public class AutomationWorker : Sitecore.Analytics.Automation.AutomationWorker
    {
        private readonly string workerId;

        public AutomationWorker() : base()
        {
            var workerIdInfo = typeof(Sitecore.Analytics.Automation.AutomationWorker)
                .GetField("workerId", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(workerIdInfo, "workerIdInfo is null.");

            workerId = workerIdInfo.GetValue(this) as string;
        }

        public override bool Process()
        {
            if (!AnalyticsSettings.Enabled)
            {
                Log.Info("AutomationWorker was not processed as Analytics is disabled.", this);
                return false;
            }

            Assert.IsNotNull(this.ContactRepository, "AutomationWorker/ContactRepository configuration does not exist");

            var contactIds = this.GetAutomationStateKeys();

            if (contactIds == null)
            {
                return false;
            }

            this.ResetDefinitionDatabase();

            var siteContext = this.GetSiteContext();

            using (new SiteContextSwitcher(siteContext))
            {
                Tracker.Initialize();
                Tracker.IsActive = true;

                var leaseOwner = new LeaseOwner(this.workerId + "_" + Thread.CurrentThread.ManagedThreadId,
                    LeaseOwnerType.OutOfRequestWorker);

                foreach (var contactId in contactIds)
                {
                    LockAttemptResult<Contact> lockAttemptResult = this.ContactRepository.TryLoadContact(contactId,
                        leaseOwner, TimeSpan.FromSeconds(10));

                    if (lockAttemptResult.Status != LockAttemptStatus.Success || lockAttemptResult.Object == null)
                    {
                        continue;
                    }

                    try
                    {
                        DateTime now = DateTime.UtcNow;

                        /*var sessionContext = new StandardSession(lockAttemptResult.Object)
                        {
                            Settings =
                            {
                                IsNew = true,
                                IsFirstRequest = true
                            }
                        };*/
                        var sessionContext = new StandardSession(lockAttemptResult.Object);
                        var settings = sessionContext.Settings;
                        this.SessionSettings_IsNew_Set(settings, true);
                        this.SessionSettings_IsFirstRequest_Set(settings, true);

                        using (new SessionSwitcher(sessionContext))
                        {
                            var automationStateManager = sessionContext.CreateAutomationStateManager();
                            var automationStates = automationStateManager.GetAutomationStates();

                            foreach (
                                var state in
                                automationStates.Where(state => state != null && state.WakeUpDateTime <= now))
                            {
                                // state.IsDue = true;
                                AutomationStateContext_IsDue_Set(state, true);
                                AutomationUpdater.BackgroundProcess(state);
                            }

                            if (sessionContext.Contact != null)
                            {
                                this.ContactRepository.SaveContact(sessionContext.Contact,
                                    new ContactSaveOptions(true, leaseOwner));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(string.Format("Cannot process EAS record of contact '{0}' by due time", contactId), ex,
                            this);
                        this.ContactRepository.ReleaseContact(contactId, leaseOwner);
                    }
                }

            }

            return true;
        }

        private IEnumerable<Guid> GetAutomationStateKeys()
        {
            var sequence = AutomationManager.Provider.GetContactIdsToProcessByWakeUpTime();

            return sequence;
        }

        private void AutomationStateContext_IsDue_Set(AutomationStateContext context, bool value)
        {
            var isDueInfo = typeof(AutomationStateContext).GetProperty("IsDue", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(isDueInfo, "isDueInfo is null...");

            isDueInfo.SetValue(context, value);
        }

        private void SessionSettings_IsNew_Set(SessionSettings settings, bool value)
        {
            var isNewInfo = typeof(SessionSettings).GetProperty("IsNew", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(isNewInfo, "isNewInfo is null...");

            isNewInfo.SetValue(settings, value);
        }

        private void SessionSettings_IsFirstRequest_Set(SessionSettings settings, bool value)
        {
            var isFirstRequestInfo = typeof(SessionSettings).GetProperty("IsFirstRequest", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(isFirstRequestInfo, "isFirstRequestInfo is null...");

            isFirstRequestInfo.SetValue(settings, value);
        }

        protected virtual SiteContext GetSiteContext()
        {
            var siteContext = SiteContext.GetSite("analytics_operations");

            if (siteContext == null)
            {
                Log.Warn("SUPPORT Can't find the Anlytics site", this);
                siteContext = SiteContext.Current;
            }

            return siteContext;
        }

        protected virtual void ResetDefinitionDatabase()
        {
            Sitecore.Context.Items["sc_definition_database"] = null;
            Sitecore.Context.Items["sc_definition_database_obj"] = null;
        }
    }
}