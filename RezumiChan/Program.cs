using Newtonsoft.Json;
using System.Diagnostics;
using RezumiChan.Models;
using System.Net.Http.Headers;
using System.Text;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using RezumiChan.Data;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Kernel.Pdf.Canvas.Parser;

namespace RezumiChanCLI
{
    public class Program
    {
        const string modelToUse = "google/gemini-3-flash-preview";
        const string endpoint = "https://openrouter.ai/api/v1/chat/completions";
        private const int leftMargin = 6;
        private const bool aiEnabled = true; // used so I can generate PDFs without using up tokens.

        private const int fontSizeMedium = 8;
        private const int fontSizeLarge = 10;

        static async Task Main(string[] args)
        {
            var jobText = File.ReadAllText("Data/job.txt");
            await RunResumePipeline(jobText);
        }

        public static string GenerateTimestamp()
        {
            // Get the current local time
            DateTime now = DateTime.Now;

            // Format the timestamp as yyyyMMdd_HHmmss
            string formattedTimestamp = now.ToString("yyyyMMdd_HHmmss");

            return formattedTimestamp;
        }

        public static async Task<string> GetJobSummary(JobPost job)
        {
            var apiKey = LoadApiKey();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var requestBody = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content =
                                $"Please provide a concise summary of the following job posting: {job.Rawtext}. Include the name of the company and the role/title. Summarize the following job posting, ensuring the summary includes every skill, skillset, requirement, and keyword mentioned. Also include what the job is about and the things you do on the job, and the products/services the company offers. Focus exclusively on job responsibilities, essential skills, qualifications, and experience requirements necessary for the role. Omit any information about benefits, compensation, equal opportunity statements, or other non-essential details. The summary should be clear, concise, and suitable for crafting a tailored resume or ATS optimization."
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                int maxRetries = 5; // Maximum number of retries
                int currentAttempt = 0;

                while (currentAttempt < maxRetries)
                {
                    currentAttempt++;

                    var response = await client.PostAsync(endpoint, content);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                        string jobSummary = result.choices[0].message.content.ToString();

                        return jobSummary;
                    }
                    else
                    {
                        Console.WriteLine(
                            $"Error calling OpenRouter API: {response.ReasonPhrase}. Attempt {currentAttempt} of {maxRetries}.");
                    }

                    // Optionally, introduce a delay before retrying
                    await Task.Delay(1000); // Wait 1 second before the next attempt
                }

                throw new Exception($"Failed to get job summary after {maxRetries} attempts.");
            }
        }


        private static void AddSkillsSection(Document document, List<Skill> skills, List<Skill> resumeSkills)
        {
            AddDivider(document, "Skills");

            Paragraph skillsParagraph = new Paragraph()
                .SetFontSize(fontSizeMedium)
                .SetMarginLeft(leftMargin)
                .SetMarginTop(6)
                .SetMarginBottom(6)          // <- controls spacing between jobs
                .SetMultipliedLeading(1.1f); // <- line spacing inside the paragraph (try 0.95f)

            foreach (var skill in skills)
            {
                string skillLine = $" ";

                foreach (var skillset in resumeSkills)
                {
                    if (skillset.Category == skill.Category)
                    {
                        foreach (var singleSkill in skillset.TopSkills)
                        {
                            skillLine += $"{singleSkill}, ";
                        }
                    }
                }

                foreach (var singleSkill in skill.Skills)
                {
                    skillLine += $"{singleSkill}, ";
                }

                if (skillLine.Length == 1)
                {
                    continue;
                }

                skillLine = skillLine.Substring(0, skillLine.Length - 2);
                skillLine += ".\n";

                skillsParagraph.Add(new Text($"{skill.Category}:").SetUnderline()); // Add name
                skillsParagraph.Add(skillLine);
            }

            document.Add(skillsParagraph);
        }

        private static void AddWorkSection(Document document, string companyName, string title, string location,
            string duration, List<string> bulletPoints)
        {
            var entry = new Paragraph()
                .SetFontSize(fontSizeMedium)
                .SetMarginLeft(leftMargin)
                .SetMarginTop(6)
                .SetMarginBottom(6)          // <- controls spacing between jobs
                .SetMultipliedLeading(1.1f); // <- line spacing inside the paragraph (try 0.95f)
            
            entry.Add(new Text($"{companyName}").SetBold());
            entry.Add(new Text($", {location} - "));
            entry.Add(new Text($"{title}").SetUnderline());
            entry.Add(new Text($", {duration}\n"));
            foreach (var point in bulletPoints)
            {
                entry.Add(new Text($" - {point}\n").SetFontSize(fontSizeMedium));
            }

            document.Add(entry);
        }

        private static void AddProjectSection(Document document, string projectName, string projectTitle,
            List<string> bulletPoints)
        {
            var entry = new Paragraph()
                .SetFontSize(fontSizeMedium)
                .SetMarginLeft(leftMargin)
                .SetMarginTop(6)
                .SetMarginBottom(6)          // <- controls spacing between jobs
                .SetMultipliedLeading(1.1f); // <- line spacing inside the paragraph (try 0.95f)
            entry.Add(new Text($"{projectName} - ").SetBold());
            entry.Add(new Text($"{projectTitle}\n").SetUnderline());

            foreach (var point in bulletPoints)
            {
                entry.Add(new Text($" - {point}\n").SetFontSize(fontSizeMedium));
            }

            document.Add(entry);
        }

        private static void AddEducationSection(Document document, Resume resume)
        {
            AddDivider(document, "Education");
            Paragraph educationEntry = new Paragraph().SetFontSize(fontSizeMedium).SetMarginLeft(leftMargin);
            ;

            for (int i = 0; i < resume.Education.Count; i++)
            {
                educationEntry.Add(new Text($"{resume.Education[i].Institution}").SetFontSize(fontSizeMedium).SetBold());
                educationEntry.Add(new Text($", {resume.Education[i].SubInstitution} - ").SetFontSize(fontSizeMedium));
                educationEntry.Add(new Text($"{resume.Education[i].Degree}").SetFontSize(fontSizeMedium).SetItalic());
                educationEntry.Add(new Text($", {resume.Education[i].Years}").SetFontSize(fontSizeMedium));

                if (i < (resume.Education.Count - 1))
                {
                    educationEntry.Add(new Text($"\n").SetFontSize(fontSizeMedium));
                }
            }

            document.Add(educationEntry);
        }

        private static void AddDivider(Document document, string sectionName)
        {
            var header = new Paragraph(sectionName)
                .SetFontSize(fontSizeLarge)
                .SetBold()
                .SetMarginLeft(leftMargin)
                .SetMarginTop(7) // <- reduce these
                .SetMarginBottom(7); // <-

            document.Add(header);
        }

        static void OpenPdf(string pdfPath)
        {
            // Check if the file exists
            if (File.Exists(pdfPath))
            {
                // Start the default PDF viewer
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true // This is required to open the file with the default application
                });

                Console.WriteLine($"PDF created at: {Path.GetFullPath(pdfPath)} and opened.");
            }
            else
            {
                Console.WriteLine("PDF file not found.");
            }
        }

        static void AddHeader(Document document, string name, string email, string phone, string city)
        {
            // Create the header paragraph
            Paragraph header = new Paragraph();
            header.Add(new Text(name + "\n")
                .SetFontSize(fontSizeLarge) // Set font size for header
                .SetBold()); // Make the header bold
            header.Add(new Text(email + " | " + phone + " | " + city).SetFontSize(fontSizeLarge));

            // Add the header to the document
            document.Add(header);
        }

        static ContextSummary GetContextSummary(string contextName, Resume resume, Stories stories, Portfolio portfolio)
        {
            string totalString = "The following is the text about this job/role on the resume: \n";

            foreach (var experience in resume.Experience)
            {
                if (experience.Context == contextName)
                {
                    totalString +=
                        $"Job: {experience.Title} at {experience.Company} ({experience.Duration})\n Description: {string.Join(" ", experience.Description)}";
                }
            }

            foreach (var project in resume.Projects)
            {
                if (project.Context == contextName)
                {
                    totalString += $"Project: {project.Name}. Description: ({string.Join(" ", project.Description)})\n";
                }
            }

            totalString += "The following is some deeper details about the projects done at the mentioned role: \n";

            foreach (var project in portfolio.Projects)
            {
                if (project.Context == contextName)
                {
                    totalString +=
                        $"{project.Title}: {project.Role} at {project.Company} ({project.Context}). This is what I did: {project.Description}. I used the following skills to do it:";
                    foreach (var tech in project.Technologies)
                    {
                        totalString += $" {tech}, ";
                    }
                }
            }

            totalString += "The following are STAR formatted stories about decisive things I did at the role: \n";

            foreach (var story in stories.Starstories)
            {
                if (story.Context == contextName)
                {
                    totalString +=
                        $"{story.Title} ({story.Context}): Situation: {story.Situation}. Task: {story.Task}. Actions:";
                    foreach (var action in story.Action)
                    {
                        totalString += $" {action}\n";
                    }

                    totalString += $". Result: {story.Result}";
                }
            }

            var newContextSummary = new ContextSummary();
            newContextSummary.ContextName = contextName;
            newContextSummary.ContextTotalText = totalString;


            string filePath = Path.Combine(Environment.CurrentDirectory, $"{contextName}.txt");

            // Save the string to "beans.txt"
            File.WriteAllText(filePath, totalString);

            // Print the path to the console
            Console.WriteLine("File saved at: " + filePath);

            return newContextSummary;
        }

        static Resume LoadResume(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                Resume resume = JsonConvert.DeserializeObject<Resume>(json);
                return resume;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading resume: {ex.Message}");
                return null;
            }
        }

        static Portfolio LoadPortfolio(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                Portfolio portfolio = JsonConvert.DeserializeObject<Portfolio>(json);
                return portfolio;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading resume: {ex.Message}");
                return null;
            }
        }

        static Stories LoadStories(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                Stories stories = JsonConvert.DeserializeObject<Stories>(json);
                return stories;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading stories: {ex.Message}");
                return null;
            }
        }

        static JobPost LoadJobPost(string filePath)
        {
            try
            {
                JobPost jobPost = new JobPost();
                jobPost.Rawtext = File.ReadAllText(filePath);
                return jobPost;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading stories: {ex.Message}");
                return null;
            }
        }

        public static string LoadApiKey()
        {
            var json = File.ReadAllText("config.json");
            var settings = JsonConvert.DeserializeObject<AppSettings>(json);
            return settings.OpenRouter.ApiKey;
        }

        public static async Task<string> GetSummaryFromOpenAI(string resumeText)
        {
            var apiKey = LoadApiKey();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var requestBody = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = $"Please summarize the following resume:\n{resumeText}"
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    return result.choices[0].message.content.ToString();
                }
                else
                {
                    throw new Exception($"Error calling OpenRouter API: {response.ReasonPhrase}");
                }
            }
        }

        public static async Task<string> GetJobName(JobPost job)
        {
            var apiKey = LoadApiKey();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var requestBody = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content =
                                $"Give me a filename-friendly name of this job including the company name (if known, if not known or looks like an anonymous recruiter, then put anonymous for the company name) and the role (only one word including underscores. do not give me any other text/paragraphs):\n{job.Summary}"
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    return result.choices[0].message.content.ToString();
                }
                else
                {
                    throw new Exception($"Error calling OpenRouter API: {response.ReasonPhrase}");
                }
            }
        }

        public static async Task<string> StartFreshWithOpenAI(string newContext)
        {
            var apiKey = LoadApiKey();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var requestBody = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = "Forget all previous contexts. I want to start a new discussion about:\n" + newContext
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    return result.choices[0].message.content.ToString();
                }
                else
                {
                    throw new Exception($"Error calling OpenRouter API: {response.ReasonPhrase}");
                }
            }
        }

        public static async Task<List<string>> GetBulletPointsFromBank(BulletpointBank bank, JobPost job, int totalNumber,
            bool current = false)
        {
            var number = totalNumber;
            if (!aiEnabled)
            {
                var examplePoints = new List<string>();
                int pointCounter = 0;
                foreach (var point in bank.RequiredBulletpoints)
                {
                    if (pointCounter != number)
                    {
                        examplePoints.Add(point.BulletpointText);
                        pointCounter++;
                    }
                }

                foreach (var point in bank.Bulletpoints)
                {
                    if (pointCounter != number)
                    {
                        examplePoints.Add(point.BulletpointText);
                        pointCounter++;
                    }
                }

                return examplePoints;
            }

            number = totalNumber - bank.RequiredBulletpoints.Count;

            StringBuilder sb = new StringBuilder();

            foreach (var bulletpoint in bank.Bulletpoints)
            {
                sb.AppendLine($"{bulletpoint.ID}) {bulletpoint.BulletpointText}");
                sb.AppendLine();
            }

            //Console.WriteLine(sb.ToString().Trim());

            var apiKey = LoadApiKey();
            string contentText =
                $"Here is the job posting: {job.Summary}. Here is a bank of bullet points from a job or project I have done: {sb.ToString().Trim()}. Return the top {number} most relevant to that job description bullet points in JSON format, using only the bullet point numbers. If two points are too strongly overlapping, do not return both, pick the strongest of the two. Do not include any additional text, explanations, or preambles- only return the JSON format. Desired output format: " +
                @"{""relevant_bullet_points"":[]}";

            //Console.WriteLine(contentText);
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var requestBody = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = contentText
                        },
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                int maxRetries = 5; // Maximum number of retries
                int currentAttempt = 0;

                while (currentAttempt < maxRetries)
                {
                    currentAttempt++;

                    var response = await client.PostAsync(endpoint, content);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                        string bulletPointsJson = result.choices[0].message.content.ToString();

                        Console.WriteLine($"bulletPointsJson: {bulletPointsJson}");

                        try
                        {
                            var deserializedResponse =
                                JsonConvert.DeserializeObject<RelevantBulletPointsResponse>(bulletPointsJson);
                            var listOfPointNumbers = deserializedResponse.RelevantBulletPoints;

                            if (listOfPointNumbers.Count != number)
                            {
                                // throw an error to force a retry
                                throw new InvalidOperationException(
                                    "The count of listOfPointNumbers does not match the expected number. Expected: " + number + ", Actual: " + listOfPointNumbers.Count);
                            }

                            if (!bank.ValidateBulletpointNumbers(listOfPointNumbers))
                            {
                                // throw an error to force a retry
                                throw new InvalidOperationException(
                                    "the list of numbers includes nonexistent bulletpoints. Returned numbers: " + string.Join(", ", listOfPointNumbers));
                            }

                            var returnPoints = new List<string>();

                            // Always include ALL required bullet points
                            foreach (var point in bank.RequiredBulletpoints)
                            {
                                returnPoints.Add(point.BulletpointText);
                            }

                            returnPoints.AddRange(bank.GetBulletpointTextsByIds(listOfPointNumbers));

                            return returnPoints;
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine(
                                $"[GetBulletPointsFromBank] Deserialization error: {ex.Message}. Attempt {currentAttempt} of {maxRetries}.");
                            Console.WriteLine($"Response content was: {bulletPointsJson}");
                        }
                        catch (InvalidOperationException ex)
                        {
                            Console.WriteLine($"[GetBulletPointsFromBank] Validation error: {ex.Message}. Attempt {currentAttempt} of {maxRetries}.");
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            $"Error calling OpenRouter API: {response.ReasonPhrase}. Attempt {currentAttempt} of {maxRetries}.");
                    }

                    // Optionally, introduce a delay before retrying
                    await Task.Delay(1000); // Wait 1 second before the next attempt
                }

                throw new Exception($"Failed to get bullet points after {maxRetries} attempts.");
            }
        }

        public static async Task<List<Skill>> GetRelevantSkills(Resume resume, JobPost job)
        {
            var apiKey = LoadApiKey();
            // --- Build skill bank ---
            var bank = new List<SkillBankEntry>();
            var nextId = 1;

            foreach (var skillset in resume.Skills)
            {
                foreach (var skill in skillset.Skills)
                {
                    bank.Add(new SkillBankEntry(
                        nextId,
                        skillset.Category,
                        skill
                    ));
                    nextId++;
                }
            }

            // --- Build numbered prompt ---
            var sb = new StringBuilder();
            foreach (var entry in bank)
            {
                sb.AppendLine($"{entry.Id}) [{entry.Category}] {entry.Skill}");
            }

            var prompt =
                $"Here is the job posting:\n{job.Summary}\n\n" +
                $"Here is my skills bank:\n{sb}\n\n" +
                $"Return:\n" +
                $"1) ALL skill categories reordered by relevance\n" +
                $"2) ALL skill IDs reordered by relevance\n\n" +
                $"Rules:\n" +
                $"- Do not add, remove, rename, or merge categories\n" +
                $"- Do not add, remove, rename, or merge skills\n" +
                $"- Use only provided category names and skill IDs\n" +
                $"- Return JSON only\n\n" +
                $"Format:\n{{\n  \"ordered_categories\": [],\n  \"ordered_skill_ids\": []\n}}";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model = modelToUse,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<dynamic>(responseText);
            var rawJson = result.choices[0].message.content.ToString();

            var parsed =
                JsonConvert.DeserializeObject<OrderedSkillsResponse>(rawJson)
                ?? throw new InvalidOperationException("Failed to parse skill ID response.");

            // --- Validation ---

            // Validate categories
            var originalCategories = resume.Skills.Select(s => s.Category).ToHashSet();

            if (parsed.OrderedCategories.Count != originalCategories.Count)
            {
                throw new InvalidOperationException("Category count mismatch.");
            }

            foreach (var category in parsed.OrderedCategories)
            {
                if (!originalCategories.Contains(category))
                {
                    throw new InvalidOperationException($"Invalid category returned: {category}");
                }
            }


            if (parsed.OrderedSkillIds.Count != bank.Count)
            {
                throw new InvalidOperationException(
                    $"Expected {bank.Count} skills, got {parsed.OrderedSkillIds.Count}."
                );
            }

            foreach (var id in parsed.OrderedSkillIds)
            {
                var found = false;

                foreach (var entry in bank)
                {
                    if (entry.Id == id)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidOperationException($"Invalid skill ID returned: {id}");
                }
            }

            // --- Rebuild grouped skills ---
            var grouped = new Dictionary<string, List<string>>();

// Initialize categories in ranked order
            foreach (var category in parsed.OrderedCategories)
            {
                grouped[category] = new List<string>();
            }

// Assign skills in ranked skill order
            foreach (var id in parsed.OrderedSkillIds)
            {
                SkillBankEntry entry = null;

                foreach (var bankEntry in bank)
                {
                    if (bankEntry.Id == id)
                    {
                        entry = bankEntry;
                        break;
                    }
                }

                if (entry == null)
                {
                    throw new InvalidOperationException($"Skill ID not found: {id}");
                }

                grouped[entry.Category].Add(entry.Skill);
            }

// Build final Skill list in category order
            var reorderedSkills = new List<Skill>();

            foreach (var category in parsed.OrderedCategories)
            {
                if (grouped[category].Count == 0)
                {
                    continue; // optional: skip empty categories
                }

                reorderedSkills.Add(new Skill(
                    category,
                    grouped[category].ToArray(),
                    null
                ));
            }

            return reorderedSkills;
        }

        public static string GetSkillsJson(Resume resume)
        {
            // Check if the resume object is not null
            if (resume == null || resume.Skills == null)
            {
                return "{}"; // Return empty JSON object if no skills are found
            }

            // Serialize the Skills property to JSON
            var skillsWithoutTopSkills = new List<Skill>();
            foreach (var skillset in resume.Skills)
            {
                skillsWithoutTopSkills.Add(new Skill(skillset.Category, skillset.Skills.ToArray(), null));
            }

            string skillsJson = JsonConvert.SerializeObject(skillsWithoutTopSkills, Formatting.Indented);
            return skillsJson;
        }

        private static string ReadPdf(string pathToPdf)
        {
            StringBuilder resumeText = new StringBuilder();

            using (PdfReader pdfReader = new PdfReader(pathToPdf))
            using (PdfDocument pdfDoc = new PdfDocument(pdfReader))
            {
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    // Extract text from each page
                    resumeText.Append(PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i)));
                }
            }

            return resumeText.ToString();
        }

        public static async Task<string> EvaluateResume(JobPost job, string resumeText)
        {
            var apiKey = LoadApiKey(); // Replace with a method that retrieves your OpenRouter API key

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var requestBody = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "You are an assistant that evaluates resume and job description matches."
                        },
                        new
                        {
                            role = "user",
                            content = $"Evaluate how well this resume matches the job description on a scale of 1 to 10, " +
                                      $"where 10 is a perfect match. Provide a one-sentence response with the score. " +
                                      $"Job Description: {job.Summary}\nResume Text: {resumeText}"
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    var resultText = result.choices[0].message.content.ToString();
                    Console.WriteLine(resultText);
                    return resultText;
                }
                else
                {
                    throw new Exception($"Error calling OpenRouter API: {response.ReasonPhrase}");
                }
            }
        }

        public static async Task<string> RunResumePipeline(
            string jobPostingText,
            IProgress<PipelineProgress>? progress = null)
        {
            Report(progress, "Loading resume data…", 5);
            Resume resume = LoadResume("Data/resume.json");
            Portfolio portfolio = LoadPortfolio("Data/portfolio.json");
            Stories stories = LoadStories("Data/stories.json");

            var job = new JobPost
            {
                Rawtext = jobPostingText
            };

            Report(progress, "Analyzing job posting…", 15);
            job.Summary = await GetJobSummary(job);

            Report(progress, "Determining job title…", 25);
            job.JobNameAndTitle = await GetJobName(job);

            Report(progress, "Loading bullet point banks…", 30);
            var hxrBank = JsonConvert.DeserializeObject<BulletpointBank>(
                File.ReadAllText("Data/Bulletpoints/howellxr_bank.json"));
            var tcBank = JsonConvert.DeserializeObject<BulletpointBank>(
                File.ReadAllText("Data/Bulletpoints/tenderclaws_bank.json"));
            var waveBank = JsonConvert.DeserializeObject<BulletpointBank>(
                File.ReadAllText("Data/Bulletpoints/wave_bank.json"));
            var bloodsportBank = JsonConvert.DeserializeObject<BulletpointBank>(
                File.ReadAllText("Data/Bulletpoints/bloodsport_bank.json"));
            var greyskiesBank = JsonConvert.DeserializeObject<BulletpointBank>(
                File.ReadAllText("Data/Bulletpoints/greyskies_bank.json"));

            Report(progress, "Selecting bullet points…", 45);
            var hxrSelected = await GetBulletPointsFromBank(hxrBank, job, 4, false);
            var tcSelected = await GetBulletPointsFromBank(tcBank, job, 3, false);
            var waveSelected = await GetBulletPointsFromBank(waveBank, job, 3, false);
            var bloodsportSelected = await GetBulletPointsFromBank(bloodsportBank, job, 3, false);
            var greyskiesSelected = await GetBulletPointsFromBank(greyskiesBank, job, 3, false);

            Report(progress, "Ordering skills…", 65);
            var skills = await GetRelevantSkills(resume, job);

            var filename =
                $"riko_balakit_resume_{job.JobNameAndTitle}_{GenerateTimestamp()}.pdf";

            Report(progress, "Generating PDF…", 85);
            using (var writer = new PdfWriter(filename))
            using (var pdf = new PdfDocument(writer))
            {
                var document = new Document(pdf);
                document.SetMargins(
                    20, // top
                    20, // right
                    20, // bottom
                    20 // left
                );

                var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                document.SetFont(font);

                AddHeader(document, "Riko Balakit", "riko@balak.it", "(210) 508-8774", "Austin, TX");
                AddSkillsSection(document, skills, resume.Skills);
                AddDivider(document, "Work Experience");
                AddWorkSection(document, "HowellXR", "Engineer (Contract)", "Austin, TX",
                    "November 2024 - December 2025", hxrSelected);
                AddWorkSection(document, "Tender Claws", "Game Developer", "Los Angeles, CA (Remote)",
                    "February 2021 - August 2024", tcSelected);
                AddWorkSection(document, "Wave (Formerly TheWaveVR)", "Engineer", "Austin, TX",
                    "February 2017 - November 2020", waveSelected);
                AddDivider(document, "Projects");
                AddProjectSection(document, "BattleBots - Bloodsport", "Telemetry Specialist",
                    bloodsportSelected);
                AddProjectSection(document, "Grey Skies Automation", "Founder, Owner, Operator",
                    greyskiesSelected);
                AddEducationSection(document, resume);
            }

            Report(progress, "Opening PDF…", 95);
            OpenPdf(filename);

            Report(progress, "Done!", 100);
            return filename;
        }



        public static List<SkillBankEntry> BuildSkillBank(Resume resume)
        {
            var bank = new List<SkillBankEntry>();
            var id = 1;

            foreach (var category in resume.Skills)
            {
                foreach (var skill in category.Skills)
                {
                    bank.Add(new SkillBankEntry(id++, category.Category, skill));
                }
            }

            return bank;
        }


        static void Report(IProgress<PipelineProgress>? progress, string message, int percent)
        {
            progress?.Report(new PipelineProgress(message, percent));
        }


        public record PipelineProgress(string Message, int Percent);

    }
}
