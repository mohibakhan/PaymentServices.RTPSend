// Adds RTPSend's subscriptions to the EXISTING shared <c>payment-processing</c>
// topic. The namespace and topic themselves are never modified by this
// template — they're provisioned by PaymentServices.Infrastructure.
//
// rtpsend-process       — filters on Subject = 'CreatePaymentRequest'; triggers
//                         ProcessPayment. RetryFailedPayments drains its DLQ.
// rtpsend-tabapay-retry — filters on Subject = 'TabaPaySendRetry'; triggers
//                         HandleTabaPayRetry for backed-off TabaPay send retries.
//
// Each subscription's CorrelationFilter ensures outcome envelopes (which use
// Subject = 'CreatePayment - Success' / 'CreatePayment - Failure') and messages
// destined for other services (AccountResolutionPending, KycPending, etc.) are
// never delivered to the wrong handler.

@description('Existing shared Service Bus namespace.')
param namespaceName string

@description('Existing shared topic name (e.g. payment-processing).')
param topicName string

@description('Name of the RTPSend subscription on the topic.')
param processSubscriptionName string

@description('Subject value the subscription filters on. Must match what CreatePayment publishes.')
param processSubject string = 'CreatePaymentRequest'

@description('Max delivery count before a message is dead-lettered.')
param processMaxDeliveryCount int = 10

@description('Lock duration on the subscription. ISO 8601 duration.')
param processLockDuration string = 'PT5M'

@description('Default message TTL on the subscription. ISO 8601 duration.')
param processDefaultTtl string = 'P14D'

@description('Name of the RTPSend TabaPay-retry subscription on the topic.')
param retrySubscriptionName string = 'rtpsend-tabapay-retry'

@description('Subject the retry subscription filters on. Must match TabaPaySendRetrySubject in code.')
param retrySubject string = 'TabaPaySendRetry'

@description('Max delivery count before a retry message is dead-lettered.')
param retryMaxDeliveryCount int = 10

@description('Lock duration on the retry subscription. ISO 8601 duration.')
param retryLockDuration string = 'PT5M'

@description('Default message TTL on the retry subscription. Must exceed the max backoff (~30 min). ISO 8601 duration.')
param retryDefaultTtl string = 'P14D'

// Reference the existing namespace and topic — never modified

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: namespaceName
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' existing = {
  parent: namespace
  name: topicName
}

// Subscription — rtpsend-process
//
// ProcessPayment triggers off this subscription.
// RetryFailedPayments drains its dead-letter sub-queue.

resource processSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: processSubscriptionName
  properties: {
    lockDuration: processLockDuration
    maxDeliveryCount: processMaxDeliveryCount
    defaultMessageTimeToLive: processDefaultTtl
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    enableBatchedOperations: true
    requiresSession: false
  }
}

// SQL filter — only deliver messages with Subject = 'CreatePaymentRequest'
//
// The default '$Default' rule (a TrueFilter that matches everything) is
// replaced by this CorrelationFilter. CorrelationFilter on Subject is the
// cheapest filter type — no SQL evaluation, just a string compare on the
// system Subject property.

resource processFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-10-01-preview' = {
  parent: processSubscription
  name: 'rtpsend-process-filter'
  properties: {
    filterType: 'CorrelationFilter'
    correlationFilter: {
      label: processSubject
    }
  }
}

// Subscription — rtpsend-tabapay-retry
//
// HandleTabaPayRetry triggers off this subscription. When a TabaPay send fails
// transiently, the message is published to the topic with Subject =
// 'TabaPaySendRetry' and a scheduled-enqueue delay (capped exponential backoff,
// up to ~30 min), so the call is retried later instead of redelivering the
// outcome message in an instant loop.

resource retrySubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: retrySubscriptionName
  properties: {
    lockDuration: retryLockDuration
    maxDeliveryCount: retryMaxDeliveryCount
    defaultMessageTimeToLive: retryDefaultTtl
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    enableBatchedOperations: true
    requiresSession: false
  }
}

// CorrelationFilter — only deliver messages with Subject = 'TabaPaySendRetry',
// replacing the default '$Default' TrueFilter so this subscription never picks
// up CreatePaymentRequest / outcome / other-service messages.

resource retryFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-10-01-preview' = {
  parent: retrySubscription
  name: 'rtpsend-tabapay-retry-filter'
  properties: {
    filterType: 'CorrelationFilter'
    correlationFilter: {
      label: retrySubject
    }
  }
}

output topicName string = topic.name
output processSubscriptionName string = processSubscription.name
output retrySubscriptionName string = retrySubscription.name
