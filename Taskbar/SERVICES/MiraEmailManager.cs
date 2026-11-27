
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KSE.LIB.EMAIL.Models;
using KSE.LIB.EMAIL.Notifications;
using KSE.LIB.EMAIL.Providers;
using KSE.LIB.EMAIL.Security;
using KSE.LIB.EMAIL.Services;

namespace Taskbar.SERVICES;

/// <summary>
/// Coordinates MIRA's email accounts, unread-count monitoring,
/// Gmail push notifications, watch renewal and reconciliation.
/// </summary>
public sealed class MiraEmailManager
{
    #region Constants

    private const string GoogleProjectId = "kse-mira";
    private const string GmailTopic = "projects/kse-mira/topics/mira-gmail-changes";
    private const string GmailSubscription = "mira-gmail-changes-sub";

    private static readonly TimeSpan WatchRenewalInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(30);

    #endregion

    #region Fields

    private readonly IReadOnlyList<EmailAccount> _accounts;
    private readonly GmailProvider _gmailProvider;
    private readonly EmailMonitor _emailMonitor;
    private readonly GooglePubSubListener _gmailListener;

    private readonly SemaphoreSlim _notificationGate = new(1, 1);

    private CancellationTokenSource? _listenerCancellation;
    private CancellationTokenSource? _microsoftCancellation;

    private Task? _microsoftListenerTask;
    private Task? _microsoftReconciliationTask;
    private Task? _listenerTask;
    private Task? _watchRenewalTask;
    private Task? _reconciliationTask;

    #endregion

    #region Events

    /// <summary>
    /// Raised when a connected account's unread count changes.
    /// </summary>
    public event Action<EmailAccountSummary>? UnreadCountChanged
    {
        add => _emailMonitor.UnreadCountChanged += value;
        remove => _emailMonitor.UnreadCountChanged -= value;
    }

    #endregion

    #region Initialisation

    /// <summary>
    /// Initialises the MIRA email manager and its configured providers.
    /// </summary>
    /// <param name="accounts">Connected email accounts.</param>
    /// <param name="additionalProviders">
    /// Additional email providers, such as Microsoft 365.
    /// </param>
    /// <example>
    /// <code>
    /// var manager = new MiraEmailManager(accounts, [microsoftProvider]);
    /// </code>
    /// </example>
    public MiraEmailManager(
        IEnumerable<EmailAccount> accounts,
        IEnumerable<IEmailProvider>? additionalProviders = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        _accounts = [.. accounts];

        var accountManager = new EmailAccountManager();

        foreach (EmailAccount account in _accounts)
            accountManager.AddAccount(account);

        string miraDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KSE", "MIRA");

        string credentialsPath = Path.Combine(miraDirectory, "google-oauth-client.json");
        string tokenDirectory = Path.Combine(miraDirectory, "GmailTokens");

        var tokenStore = new WindowsEncryptedGoogleDataStore(tokenDirectory);

        _gmailProvider = new GmailProvider(credentialsPath, tokenStore);

        var providers = new List<IEmailProvider> { _gmailProvider };

        if (additionalProviders is not null)
            providers.AddRange(additionalProviders);

        var emailService = new EmailService(accountManager, providers);

        _emailMonitor = new EmailMonitor(emailService);
        _gmailListener = new GooglePubSubListener(GoogleProjectId, GmailSubscription);
    }

    #endregion

    #region Email Operations

    /// <summary>
    /// Refreshes an account's unread count and raises a change event
    /// only when the reported count differs.
    /// </summary>
    /// <param name="accountId">Identifier of the account to refresh.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The latest unread-count summary.</returns>
    /// <example>
    /// <code>
    /// var summary = await manager.GetUnreadSummaryAsync("personal");
    /// </code>
    /// </example>
    public Task<EmailAccountSummary> GetUnreadSummaryAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return _emailMonitor.RefreshAccountAsync(accountId, cancellationToken);
    }

    /// <summary>
    /// Serialises background Gmail refreshes to prevent overlapping
    /// requests from push notifications and reconciliation.
    /// </summary>
    /// <param name="account">The Gmail account to refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated unread-count summary.</returns>
    /// <example>
    /// <code>
    /// var summary = await RefreshGmailAccountAsync(account, cancellationToken);
    /// </code>
    /// </example>
    private async Task<EmailAccountSummary> RefreshGmailAccountAsync(
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        await _notificationGate.WaitAsync(cancellationToken);

        try
        {
            return await _emailMonitor.RefreshAccountAsync(account.Id, cancellationToken);
        }
        finally
        {
            _notificationGate.Release();
        }
    }

    #endregion

    #region Gmail Notification Lifecycle

    /// <summary>
    /// Starts the Gmail Pub/Sub listener, registers mailbox watches
    /// and starts the background maintenance tasks.
    /// </summary>
    /// <example>
    /// <code>
    /// await manager.StartGmailNotificationsAsync();
    /// </code>
    /// </example>
    public async Task StartGmailNotificationsAsync()
    {
        if (_listenerTask is not null)
            return;

        EmailAccount[] gmailAccounts = [.. _accounts.Where(account => account.Provider == EmailProviderType.Gmail)];

        if (gmailAccounts.Length == 0)
            return;

        _listenerCancellation = new CancellationTokenSource();

        CancellationToken cancellationToken = _listenerCancellation.Token;

        // Start listening before registering watches so initial
        // Gmail notifications can be received.
        _listenerTask = _gmailListener.ListenAsync(
            HandleGmailNotificationAsync, cancellationToken);

        _ = ObserveListenerAsync(_listenerTask);

        // Register each configured Gmail mailbox.
        foreach (EmailAccount account in gmailAccounts)
        {
            try
            {
                var watch = await _gmailProvider.StartWatchAsync(
                    account, GmailTopic, cancellationToken);

                Debug.WriteLine(
                    $"Gmail watch registered: {account.DisplayName}, " +
                    $"history ID: {watch.HistoryId}, " +
                    $"expiration: {watch.Expiration}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Gmail watch registration failed for {account.DisplayName}: {ex}");
            }
        }

        // Background maintenance runs independently of incoming events.
        _watchRenewalTask = RenewGmailWatchesAsync(gmailAccounts, cancellationToken);
        _reconciliationTask = ReconcileGmailAccountsAsync(gmailAccounts, cancellationToken);

        Debug.WriteLine(
            $"MIRA Gmail monitoring started. " +
            $"Reconciliation interval: {ReconciliationInterval.TotalMinutes} minutes.");
    }

    /// <summary>
    /// Stops the Pub/Sub listener and all Gmail maintenance tasks.
    /// </summary>
    /// <example>
    /// <code>
    /// await manager.StopGmailNotificationsAsync();
    /// </code>
    /// </example>
    public async Task StopGmailNotificationsAsync()
    {
        if (_listenerCancellation is null || _listenerTask is null)
            return;

        CancellationTokenSource cancellation = _listenerCancellation;

        cancellation.Cancel();

        try
        {
            await Task.WhenAll(
                _listenerTask,
                _watchRenewalTask ?? Task.CompletedTask,
                _reconciliationTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
        finally
        {
            cancellation.Dispose();

            _listenerCancellation = null;
            _listenerTask = null;
            _watchRenewalTask = null;
            _reconciliationTask = null;

            Debug.WriteLine("MIRA Gmail monitoring stopped.");
        }
    }

    #endregion

    #region Gmail Push Notification Handling

    /// <summary>
    /// Routes an incoming Gmail Pub/Sub notification to the matching
    /// account and refreshes its unread count.
    /// </summary>
    /// <param name="message">Decoded Pub/Sub message body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the message can be acknowledged.</returns>
    private async Task<bool> HandleGmailNotificationAsync(
        string message,
        CancellationToken cancellationToken)
    {
        if (!GmailPushNotification.TryParse(message, out var notification))
            return true;

        EmailAccount? account = _accounts.FirstOrDefault(a =>
            a.Provider == EmailProviderType.Gmail &&
            string.Equals(
                a.EmailAddress,
                notification!.EmailAddress,
                StringComparison.OrdinalIgnoreCase));

        if (account is null)
        {
            Debug.WriteLine(
                $"Ignored Gmail notification for unknown account: {notification!.EmailAddress}");

            return true;
        }

        try
        {
            EmailAccountSummary summary = await RefreshGmailAccountAsync(
                account, cancellationToken);

            Debug.WriteLine(
                $"Gmail push processed: {account.DisplayName}, " +
                $"{summary.UnreadCount} unread, " +
                $"history ID: {notification!.HistoryId}");

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Gmail push refresh failed for {account.DisplayName}: {ex}");

            return false;
        }
    }

    #endregion

    #region Gmail Watch Renewal

    /// <summary>
    /// Renews Gmail mailbox watches every 24 hours.
    /// Individual account failures do not stop other renewals.
    /// </summary>
    /// <param name="accounts">Configured Gmail accounts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <example>
    /// <code>
    /// await RenewGmailWatchesAsync(gmailAccounts, cancellationToken);
    /// </code>
    /// </example>
    private async Task RenewGmailWatchesAsync(
        IReadOnlyList<EmailAccount> accounts,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(WatchRenewalInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (EmailAccount account in accounts)
                {
                    try
                    {
                        var watch = await _gmailProvider.StartWatchAsync(
                            account, GmailTopic, cancellationToken);

                        Debug.WriteLine(
                            $"Gmail watch renewed: {account.DisplayName}, " +
                            $"expiration: {watch.Expiration}");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"Gmail watch renewal failed for {account.DisplayName}: {ex}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
    }

    #endregion

    #region Gmail Reconciliation

    /// <summary>
    /// Periodically reconciles Gmail unread counts against the server.
    /// This provides a fallback when Pub/Sub notifications are delayed
    /// or missed, without requiring continuous API polling.
    /// </summary>
    /// <param name="accounts">Configured Gmail accounts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <example>
    /// <code>
    /// await ReconcileGmailAccountsAsync(gmailAccounts, cancellationToken);
    /// </code>
    /// </example>
    private async Task ReconcileGmailAccountsAsync(
        IReadOnlyList<EmailAccount> accounts,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ReconciliationInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Debug.WriteLine("Starting scheduled Gmail reconciliation.");

                foreach (EmailAccount account in accounts)
                {
                    try
                    {
                        EmailAccountSummary summary = await RefreshGmailAccountAsync(
                            account, cancellationToken);

                        Debug.WriteLine(
                            $"Gmail reconciled: {account.DisplayName}, " +
                            $"{summary.UnreadCount} unread.");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"Gmail reconciliation failed for {account.DisplayName}: {ex}");
                    }
                }

                Debug.WriteLine("Scheduled Gmail reconciliation completed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
    }

    #endregion

    #region Listener Diagnostics

    /// <summary>
    /// Observes and logs unexpected Pub/Sub listener failures
    /// without blocking MIRA's user interface.
    /// </summary>
    /// <param name="listenerTask">The listener task to observe.</param>
    private static async Task ObserveListenerAsync(Task listenerTask)
    {
        try
        {
            await listenerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when the listener is cancelled.
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Google Pub/Sub listener stopped unexpectedly: {ex}");
        }
    }

    #endregion

    #region Microsoft 365 Notifications

    /// <summary>
    /// Starts IMAP IDLE monitoring and periodic reconciliation for
    /// all configured Microsoft 365 email accounts.
    /// </summary>
    /// <param name="provider">The authenticated Microsoft 365 provider.</param>
    /// <example>
    /// <code>
    /// manager.StartMicrosoftNotifications(microsoftProvider);
    /// </code>
    /// </example>
    public void StartMicrosoftNotifications(Microsoft365Provider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (_microsoftListenerTask is not null)
            return;

        EmailAccount[] accounts = [.. _accounts.Where(account => account.Provider == EmailProviderType.Microsoft365)];

        if (accounts.Length == 0)
            return;

        _microsoftCancellation = new CancellationTokenSource();
        CancellationToken token = _microsoftCancellation.Token;

        _microsoftListenerTask = Task.WhenAll(
            accounts.Select(account => MonitorMicrosoftAccountAsync(provider, account, token)));

        _microsoftReconciliationTask = ReconcileMicrosoftAccountsAsync(accounts, token);

        _ = ObserveMicrosoftListenerAsync(_microsoftListenerTask);

        Debug.WriteLine($"Microsoft 365 monitoring started for {accounts.Length} account(s).");
    }

    /// <summary>
    /// Maintains an IMAP IDLE connection for a Microsoft 365 account,
    /// reconnecting if the connection is interrupted.
    /// </summary>
    private async Task MonitorMicrosoftAccountAsync(
        Microsoft365Provider provider,
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var listener = new MicrosoftImapListener();

                listener.Connected += () =>
                    Debug.WriteLine($"Microsoft IMAP connected: {account.DisplayName}");

                await listener.ListenAsync(
                    account.EmailAddress,
                    token => provider.GetImapAccessTokenAsync(account, token),
                    async token =>
                    {
                        EmailAccountSummary summary = await RefreshMonitoredAccountAsync(account, token);

                        Debug.WriteLine(
                            $"Microsoft IMAP update: {account.DisplayName}, " +
                            $"{summary.UnreadCount} unread.");
                    },
                    cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                    Debug.WriteLine($"Microsoft IMAP connection ended: {account.DisplayName}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Microsoft IMAP error for {account.DisplayName}: {ex}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Refreshes an email account through EmailMonitor while preventing
    /// overlapping background refresh operations.
    /// </summary>
    private async Task<EmailAccountSummary> RefreshMonitoredAccountAsync(
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        await _notificationGate.WaitAsync(cancellationToken);

        try
        {
            return await _emailMonitor.RefreshAccountAsync(account.Id, cancellationToken);
        }
        finally
        {
            _notificationGate.Release();
        }
    }

    /// <summary>
    /// Reconciles Microsoft 365 unread counts every 30 minutes to
    /// recover changes missed while IMAP is disconnected.
    /// </summary>
    private async Task ReconcileMicrosoftAccountsAsync(
        IReadOnlyList<EmailAccount> accounts,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (EmailAccount account in accounts)
                {
                    try
                    {
                        EmailAccountSummary summary = await RefreshMonitoredAccountAsync(account, cancellationToken);

                        Debug.WriteLine(
                            $"Microsoft reconciliation: {account.DisplayName}, " +
                            $"{summary.UnreadCount} unread.");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Microsoft reconciliation failed for {account.DisplayName}: {ex}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Stops Microsoft 365 IMAP monitoring and background reconciliation.
    /// </summary>
    /// <example>
    /// <code>
    /// await manager.StopMicrosoftNotificationsAsync();
    /// </code>
    /// </example>
    public async Task StopMicrosoftNotificationsAsync()
    {
        if (_microsoftCancellation is null)
            return;

        CancellationTokenSource cancellation = _microsoftCancellation;
        cancellation.Cancel();

        try
        {
            await Task.WhenAll(
                _microsoftListenerTask ?? Task.CompletedTask,
                _microsoftReconciliationTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        finally
        {
            cancellation.Dispose();

            _microsoftCancellation = null;
            _microsoftListenerTask = null;
            _microsoftReconciliationTask = null;

            Debug.WriteLine("Microsoft 365 monitoring stopped.");
        }
    }

    /// <summary>
    /// Logs unexpected termination of Microsoft 365 monitoring.
    /// </summary>
    private static async Task ObserveMicrosoftListenerAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Microsoft 365 monitoring stopped unexpectedly: {ex}");
        }
    }

    #endregion

}
