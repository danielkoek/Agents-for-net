﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using Microsoft.Agents.CopilotStudio.Client;
using Microsoft.Extensions.AI;
using Microsoft.Agents.Core.Models;

#nullable disable

namespace EvalClient;

/// <summary>
/// This class is responsible for handling the Evaluation service and managing the conversation with the Copilot Studio hosted Agent and Azure OpenAI.
/// </summary>
/// <param name="copilotClient">Connection Settings for connecting to Copilot Studio</param>
/// <param name="chatClient">Connection Settings for connecting to Azure OpenAI</param>

internal class EvaluationService(EvalClientConfig settings, CopilotClient copilotClient, IChatClient chatClient) : IHostedService
{
    /// <summary>
    /// This is the main thread loop that manages the back and forth communication with the Copilot Studio Agent. 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var inputEvalDataset = $"./Data/{settings.EvaluationDataset}";
        var outputEvalDataset = $"./Data/{settings.EvaluationDataset}_Results.csv";
        
        // Load the input file and parse the data.
        var evalDataset = LoadCsvData(inputEvalDataset);

        
        Console.WriteLine("\nRunning evaluation on Agent");
        // Attempt to connect to the copilot studio hosted Agent here
        // if successful, this will loop though all events that the Copilot Studio Agent sends to the client setup the conversation.
        await foreach (IActivity act in copilotClient.StartConversationAsync(emitStartConversationEvent: true, cancellationToken: cancellationToken))
        {
            if (act is null)
            {
                throw new InvalidOperationException("Activity is null");
            }
            // for each response, report to the UX
            GetActivity(act);
        }

        // Once we are connected and have initiated the conversation, begin the message loop with the Console.
        for (var count = 0; count < evalDataset.Count(); count++)
        {
            Console.WriteLine($"Evaluating dataset {count + 1}/{evalDataset.Count()}...");
            var question = evalDataset[count].TestUtterance;

            // Send the evaluation question to the Copilot Studio Agent and await the response.
            var response = String.Empty;
            await foreach (Activity act in copilotClient.AskQuestionAsync(question, null, cancellationToken))
            {
                response += GetActivity(act);
            }
            if (!string.IsNullOrEmpty(response))
            {
                evalDataset[count].AgentResponse = response.Replace(",", "");
                evalDataset[count].AnswerScore = await EvaluateResponse(evalDataset[count]);
                evalDataset[count].SourcesScore = EvaluateSourcesScore(evalDataset[count]);
            }
        }

        SaveCsvData(evalDataset, outputEvalDataset);
        Console.WriteLine($"\nEvaluation complete. Output in {outputEvalDataset}");
    }

    /// <summary>
    /// This method is responsible for writing formatted data to the console.
    /// This method does not handle all of the possible activity types and formats, it is focused on just a few common types. 
    /// </summary>
    /// <param name="act"></param>
    /// <returns name="response"></returns>
    private async Task<string> EvaluateResponse(EvalDataset evalDataPoint)
    {
        var groundednessEvaluationPrompt = settings.GroundednessEvaluationPrompt;
        var chatHistory = new List<ChatMessage>();

        // Run the Evaluation against the OpenAI Client
        // Use default system prompt if none is provided
        var systemPrompt = (string.IsNullOrEmpty(groundednessEvaluationPrompt)) ? """
                               You are tasked to evaluate the quality of responses generated by a Copilot Studio Agent against a ground truth.
                               You will get 3 values. One is called 'Question' and contains the question asked.
                               One is called 'Ground truth' and contains the the expected response.
                               The third is called 'Agent response' and contains the response generated by the Copilot Studio Agent.
                               Compare the "Agent response" against the "Ground truth" and provide a score between 0 and 100 where 0 is the worst possible match and 100 is a perfect match.
                               Do not make up any information, only use the information provided in the "Agent response" and "Ground truth" fields.
                               Do not provide any additional information or commentary.
                           """ : groundednessEvaluationPrompt;

        chatHistory.Add(new ChatMessage(ChatRole.System, systemPrompt));
        
        // Get parameters from evaluation dataset and add as user prompt to chat history
        var userPrompt = $"""
                    Question: '{evalDataPoint.TestUtterance}'
                    Ground truth: '{evalDataPoint.ExpectedResponse}'
                    Agent response: '{evalDataPoint.AgentResponse}'
                """;
            
        chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

        // Stream the AI response and add to chat history
        var response = "";
        await foreach (var item in  chatClient.CompleteStreamingAsync(chatHistory))
        {
            response += item.Text;
        }

        return response;
    }
    
    /// <summary>
    /// This method evaluates the agent's response to see if the sources links returned match the expected sources provided in the ground truth.
    /// </summary>
    /// <param name="evalDataPoint"></param>
    /// <returns name="string"></returns>
    private string EvaluateSourcesScore(EvalDataset evalDataPoint)
    {
        var sourceUrls = evalDataPoint.Sources.Split(';').ToList();
        var urlFound = 0;
        
        foreach (var sourceUrl in sourceUrls)
        {
            if (evalDataPoint.AgentResponse.Contains(sourceUrl))
            {
                urlFound += 1;
            }
        }

        return $"{urlFound}/{sourceUrls.Count()}";
    }
    
    /// <summary>
    /// This method loads the input CSV file and returns it as a list of EvalDataset.
    /// </summary>
    /// <param name="csvFilePath"></param>
    /// <returns name="evalDataset"></returns>
    private List<EvalDataset> LoadCsvData(string csvFilePath)
    {
        List<EvalDataset> evalDataset = new List<EvalDataset>();
        try
        {
            using (var reader = new StreamReader(csvFilePath))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            }))
            {
                csv.Context.RegisterClassMap<EvalDatasetCsvMap>();
                evalDataset = csv.GetRecords<EvalDataset>().ToList();
                
                Console.WriteLine($"Total evaluation questions Loaded: {evalDataset.Count}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }

        return evalDataset;
    }
    
    /// <summary>
    /// This method saves the output CSV file from a list of EvalDataset.
    /// </summary>
    /// <param name="evalDataset"></param>
    /// <param name="csvFilePath"></param>

    private void SaveCsvData(List<EvalDataset> evalDataset, string csvFilePath)
    {
        try
        {
            using (var writer = new StreamWriter(csvFilePath))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            }))
            {
                csv.Context.RegisterClassMap<EvalDatasetResultCsvMap>();
                csv.WriteRecords(evalDataset);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred when creating the output CSV file: {ex.Message}");
        }
    }
    
    /// <summary>
    /// This method is responsible for writing formatted data to the console.
    /// This method does not handle all of the possible activity types and formats, it is focused on just a few common types. 
    /// </summary>
    /// <param name="act"></param>
    /// <returns name="response"></returns>
    
    static string GetActivity(IActivity act)
    {
        var response = "";
        
        if (act.Type == "message")
        {
            response = act.Text;
        }
        
        return response;
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
