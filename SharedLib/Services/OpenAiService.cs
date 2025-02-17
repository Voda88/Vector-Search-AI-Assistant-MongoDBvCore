﻿using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SharedLib.Services;

/// <summary>
/// Service to access Azure OpenAI.
/// </summary>
public class OpenAiService
{
    private readonly string _embeddingsModelOrDeployment = string.Empty;
    private readonly string _completionsModelOrDeployment = string.Empty;
    private readonly int _maxConversationTokens = default;
    private readonly int _maxCompletionTokens = default;
    private readonly int _maxEmbeddingTokens = default;
    private readonly ILogger _logger;
    private readonly OpenAIClient _client;



    //System prompts to send with user prompts to instruct the model for chat session

    private readonly string _systemPromptRetailAssistant = @"
        You are an intelligent assistant for the Cosmic Works Bike Company. 
        You are designed to provide helpful answers to user questions about 
        product, product category, customer and sales order information provided in JSON format below.

        Instructions:
        - Only answer questions related to the information provided below,
        - Don't reference any product, customer, or salesOrder data not provided below.
        - If you're unsure of an answer, you can say ""I don't know"" or ""I'm not sure"" and recommend users search themselves.

        Text of relevant information:";

    //System prompt to send with user prompts to instruct the model for summarization
    private readonly string _summarizePrompt = @"
        Summarize the text below in one or two words to use as a label in a button on a web page. Output words only. Summarize the text below here:" + Environment.NewLine;


    /// <summary>
    /// Gets the maximum number of tokens from the conversation to send as part of the user prompt.
    /// </summary>
    public int MaxConversationTokens
    {
        get => _maxConversationTokens;
    }
    /// <summary>
    /// Gets the maximum number of tokens that can be used in generating the completion.
    /// </summary>
    public int MaxCompletionTokens
    {
        get => _maxCompletionTokens; 
    }

    /// <summary>
    /// Gets the maximum number of tokens that can be used in generating embeddings.
    /// </summary>
    public int MaxEmbeddingTokens
    {
        get => _maxEmbeddingTokens;
    }

    /// <summary>
    /// Creates a new instance of the service.
    /// </summary>
    /// <param name="endpoint">Endpoint URI.</param>
    /// <param name="key">Account key.</param>
    /// <param name="embeddingsDeployment">Name of the model deployment for generating embeddings.</param>
    /// <param name="completionsDeployment">Name of the model deployment for generating completions.</param>
    /// <param name="maxConversationBytes">Maximum number of bytes to limit conversation history sent for a completion.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when endpoint, key, deploymentName, or maxConversationBytes is either null or empty.</exception>
    /// <remarks>
    /// This constructor will validate credentials and create a HTTP client instance.
    /// </remarks>
    public OpenAiService(string endpoint, string key, string embeddingsDeployment, string completionsDeployment, string maxCompletionTokens, string maxConversationTokens, string maxEmbeddingTokens, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(embeddingsDeployment);
        ArgumentException.ThrowIfNullOrEmpty(completionsDeployment);
        ArgumentException.ThrowIfNullOrEmpty(maxConversationTokens);
        ArgumentException.ThrowIfNullOrEmpty(maxCompletionTokens);
        ArgumentException.ThrowIfNullOrEmpty(maxEmbeddingTokens);

        _embeddingsModelOrDeployment = embeddingsDeployment;
        _completionsModelOrDeployment = completionsDeployment;
        _maxConversationTokens = int.TryParse(maxConversationTokens, out _maxConversationTokens) ? _maxConversationTokens : 100;
        _maxCompletionTokens = int.TryParse(maxCompletionTokens, out _maxCompletionTokens) ? _maxCompletionTokens : 500;
        _maxEmbeddingTokens = int.TryParse(maxEmbeddingTokens, out _maxEmbeddingTokens) ? _maxEmbeddingTokens : 8000;

        _logger = logger;

        OpenAIClientOptions options = new OpenAIClientOptions()
        {
            Retry =
            {
                Delay = TimeSpan.FromSeconds(2),
                MaxRetries = 10,
                Mode = RetryMode.Exponential
            }
        };

        //Use this as endpoint in configuration to use non-Azure Open AI endpoint and OpenAI model names
        if (endpoint.Contains("api.openai.com"))
            _client = new OpenAIClient(key, options);
        else
            _client = new(new Uri(endpoint), new AzureKeyCredential(key), options);
        

    }

    /// <summary>
    /// Sends a prompt to the deployed OpenAI embeddings model and returns an array of vectors as a response.
    /// </summary>
    /// <param name="sessionId">Chat session identifier for the current conversation.</param>
    /// <param name="prompt">Prompt message to generated embeddings on.</param>
    /// <returns>Response from the OpenAI model as an array of vectors along with tokens for the prompt and response.</returns>
    public async Task<(float[] vectors, int promptTokens)> GetEmbeddingsAsync(string sessionId, string input)
    {

        float[] embedding = new float[0];
        int responseTokens = 0;

        try
        {
            EmbeddingsOptions options = new EmbeddingsOptions()
            {
                Input = new List<string> { input },
                DeploymentName = _embeddingsModelOrDeployment,
                User = sessionId
            };


            var response = await _client.GetEmbeddingsAsync(options);


            Embeddings embeddings = response.Value;

            
            responseTokens = embeddings.Usage.TotalTokens;

            embedding = embeddings.Data[0].Embedding.ToArray();

            return (embedding, responseTokens);
        }
        catch (Exception ex)
        {
            string message = $"OpenAiService.GetEmbeddingsAsync(): {ex.Message}";
            _logger.LogError(message);
            throw;
            
        }
    }

    /// <summary>
    /// Sends a prompt to the deployed OpenAI LLM model and returns the response.
    /// </summary>
    /// <param name="sessionId">Chat session identifier for the current conversation.</param>
    /// <param name="prompt">Prompt message to send to the deployment.</param>
    /// <returns>Response from the OpenAI model along with tokens for the prompt and response.</returns>
    public async Task<(string response, int promptTokens, int responseTokens)> GetChatCompletionAsync(string sessionId, string userPrompt, string documents)
    {

        try
        {
        
            ChatRequestSystemMessage systemMessage = new ChatRequestSystemMessage(_systemPromptRetailAssistant + documents);
            ChatRequestUserMessage userMessage = new ChatRequestUserMessage(userPrompt);


            ChatCompletionsOptions options = new()
            {
                DeploymentName = _completionsModelOrDeployment,
                Messages =
                {
                    systemMessage,
                    userMessage
                },
                MaxTokens = _maxCompletionTokens,
                User = sessionId,
                Temperature = 0.3f,
                NucleusSamplingFactor = 0.95f,
                FrequencyPenalty = 0,
                PresencePenalty = 0
            };

            Response<ChatCompletions> completionsResponse = await _client.GetChatCompletionsAsync(options);
        

            ChatCompletions completions = completionsResponse.Value;

            return (
                response: completions.Choices[0].Message.Content,
                promptTokens: completions.Usage.PromptTokens,
                responseTokens: completions.Usage.CompletionTokens
            );

        }
        catch ( Exception ex ) 
        {

            string message = $"OpenAiService.GetChatCompletionAsync(): {ex.Message}";
            _logger.LogError(message);
            throw;

        }
    }

    /// <summary>
    /// Sends the existing conversation to the OpenAI model and returns a two word summary.
    /// </summary>
    /// <param name="sessionId">Chat session identifier for the current conversation.</param>
    /// <param name="userPrompt">The first User Prompt and Completion to send to the deployment.</param>
    /// <returns>Summarization response from the OpenAI model deployment.</returns>
    public async Task<string> SummarizeAsync(string sessionId, string userPrompt)
    {

        ChatRequestSystemMessage systemMessage = new ChatRequestSystemMessage(_summarizePrompt);
        ChatRequestUserMessage userMessage = new ChatRequestUserMessage(userPrompt);

        ChatCompletionsOptions options = new()
        {
            DeploymentName = _completionsModelOrDeployment,
            Messages = {
                systemMessage,
                userMessage
            },
            User = sessionId,
            MaxTokens = 200,
            Temperature = 0.0f,
            NucleusSamplingFactor = 1.0f,
            FrequencyPenalty = 0,
            PresencePenalty = 0
        };

        Response<ChatCompletions> completionsResponse = await _client.GetChatCompletionsAsync(options);

        ChatCompletions completions = completionsResponse.Value;
        string output = completions.Choices[0].Message.Content;

        //Remove all non-alpha numeric characters in case the model returns any
        string summary = Regex.Replace(output, @"[^a-zA-Z0-9\s]", "");

        return summary;
    }
}
