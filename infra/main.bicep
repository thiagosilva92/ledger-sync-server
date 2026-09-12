// Infrastructure as Code for ledger-sync-server's Azure deployment.
//
// This is a *reproducibility* document as much as a deployment tool: every
// resource here was originally created by hand with individual `az` CLI
// commands (see README.md's "Deployment" section for that history). This
// template captures the end result so the whole resource group could be
// recreated from scratch without re-deriving those steps.
//
// Routine app deploys (new image after a merge to main) do NOT run this
// template — see .github/workflows/cd.yaml, which only pushes a new image
// and points the existing Container App at it. This template is applied
// deliberately via .github/workflows/infra.yaml (workflow_dispatch only),
// the same way a team would gate infrastructure changes behind a human
// decision separate from routine app releases.
targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Name of the Container Apps environment.')
param environmentName string = 'ledger-sync-env'

@description('Name of the Azure Container Registry. Must be globally unique, alphanumeric only.')
param acrName string = 'ledgersyncacr2026'

@description('Name of the PostgreSQL Flexible Server. Must be globally unique.')
param postgresServerName string = 'ledger-sync-db'

@description('Name of the application database inside the PostgreSQL server.')
param postgresDatabaseName string = 'ledger_sync'

@description('PostgreSQL administrator login name.')
param postgresAdminLogin string = 'syncadmin'

@description('PostgreSQL administrator password. Required — has no default on purpose, so it can never be committed to source control.')
@secure()
param postgresAdminPassword string

@description('Name of the Container App running the API.')
param containerAppName string = 'ledger-sync-api'

@description('Full image reference to deploy, e.g. ledgersyncacr2026.azurecr.io/ledger-sync-api:sha-abc123.')
param containerImage string = '${acrName}.azurecr.io/ledger-sync-api:latest'

@description('SHA-256 hash of the API key clients authenticate with (never the raw key). See src/Ledger.SyncServer/Authentication/ApiKeyHasher.cs.')
@secure()
param apiKeyHash string

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${environmentName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource containerAppEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// Trial-subscription-friendly access rule: no VNet integration is set up
// (that's real cost/complexity for a portfolio project's actual needs), so
// the Container App reaches Postgres over its public endpoint via this
// "allow Azure services" rule, same as it does live today.
resource postgresFirewallAllowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: postgresDatabaseName
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'Auto'
      }
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
        {
          name: 'db-connection-string'
          value: 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${postgresDatabaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require;Trust Server Certificate=true'
        }
        {
          name: 'api-key-hash-0'
          value: apiKeyHash
        }
      ]
    }
    template: {
      containers: [
        {
          name: containerAppName
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ConnectionStrings__SyncDatabase'
              secretRef: 'db-connection-string'
            }
            {
              name: 'ApiKeys__Hashes__0'
              secretRef: 'api-key-hash-0'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output acrLoginServer string = acr.properties.loginServer
