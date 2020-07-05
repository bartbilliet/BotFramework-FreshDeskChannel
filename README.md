# Custom FreshDesk channel for the Bot Framework
Quick draft. Code can definitely still be optimized. 

## Goal
- Provide a community-driven custom channel for the Microsoft Bot Framework to allow reading from, and sending responses to, support tickets in the [FreshDesk customer service application](https://freshdesk.com/). 

## Solution
- The FreshDesk custom channel for Bot Framework is implemented as an Azure function. When called, it checks for any new tickets or new ticket responses, and passes the ticket subject & description along to the Microsoft Bot Framework. 
- The custom FreshDesk channel can be invoked in two ways: 
  - Via a Timer-based trigger, which will poll the FreshDesk API every x time to fetch new tickets/responses. This method works for most FreshDesk plans that supports API calls. 
  - Via a HTTP trigger, which can be configured in specific FreshDesk plans as a webhook that is called when a ticket is updated. 
- The FreshDesk channel will use 2 containers on the same CosmosDB instance than the one used by Bot Framework:
  - *Freshdesk-botstate-container:* Link FreshDesk tickets to conversations happening in the Bot Framework
  - *LastRun*: Keep track of last execution (only processes differentials to save API limits)

## How to set things up
- Install your Bot logic (using BotFramework) on Azure. This should give you following components in Azure: App Service, CosmosDB, StorageAccount, Cognitive services (LUIS). 
- Add the [Direct Line channel](https://docs.microsoft.com/en-us/azure/bot-service/bot-service-channel-connect-directline?view=azure-bot-service-4.0) to your Bot in Azure. Copy the secret to **DirectLineSecret** and the name of the bot to **BotId** in the settings.json. 
- Add the CosmosDB **CosmosDBEndpointUri**, **CosmosDBPrimaryKey**, **CosmosDBDatabaseId**, **CosmosDBContainerId** connection information to the settings.json.  
- Add the FreshDesk **FreshDeskClientUrl** and **FreshDeskAPIKey** to the Settings.json.
- Configure the number of days the channel will listen for proactive messages from the Bot (without new incoming messages on the ticket in FreshDesk) via **MaxDaysToWaitForBotResponses** in the settings.json. To always poll *ALL* conversations for possible proactive messages from the bot, set this value to 0. This will result in slower run cycles when the bot has processed many conversations.
- Only newly updated tickets after the initial run will be processed, existing tickets will not be handled by the FreshDesk channel for Bot Framework. 

## Current features
- Send bot responses back to customer as a ticket reply. This is the default bot response.
- Add a private note for human engineer, instead of immediate responses to customer (via bot ChannelData).
  To send a private note, and optionally notify an engineer, send following in the ChannelData with the bot message. The email addresses used must be valid agents registered in FreshDesk, otherwise the message will fail.
  ```json
    {
      "MessageType": "note",
      "Private": true,
      "NotifyEmails": ["agent.1@domain.com", "agent.2@domain.com"]
    }
  ```  
- Update the ticket status after bot reply. By default the bot will set the status to 'pending' after each reply. 
  To set a different status after the bot response, use following JSON as ChannelData in your bot response. The values to be used correspond to the [official FreshDesk API](https://developers.freshdesk.com/api/#update_ticket). 
  Updating the ticket state is currently only possible on a 'reply' MessageType. 
  ```json
    {
      "Message": "YourMessage",
      "MessageType": "reply",
      "Status": 4
    }
  ```  
- The channel can continue listening for [proactive bot messages](https://docs.microsoft.com/en-us/azure/bot-service/bot-builder-howto-proactive-message) for a configurable number of days after the last message on the FreshDesk ticket was received.
- Extensibility for pre-processing of messages before they are sent to the Bot Framework, and post-processing on the bot responses. This allows for extensibility by sending messages through a Logic App connector, Azure Function, or other API integrations and can be used in scenarios such as for example translating bot messages. 
  The extensibility is offered by setting the keys **PreProcessingExtensibility** and **PostProcessingExtensibility** in the settings.json to the URL of your API which should accept the POST method. 
  
  The **pre-processing** extensibility API will receive following object as POST data: 
    ```json
  {
        "TicketId": int,
        "Subject": string,
        "Message": string,
        "Group_id": int,
        "Responder_id": int,
        "Source": int,
        "Company_id": int,
        "Status": int,
        "Product_id": int,
        "Due_by": string,
        "MessageType": string,
        "Private": boolean,
        "RequesterName": string,
        "FromEmail": string,
        "Mobile": string,
        "Phone": string
  }
  ```

  The **post-processing** extensibility API will receive the following object as POST data:
  ```json
  {
    "MessageType": string,
    "Message": string,
    "Private": boolean,
    "NotifyEmails": array of strings,
    "Status": int
  }
  ```

## Upcoming features
- Send custom activities to the bot when the ticket assignment or ticket status is updated.

