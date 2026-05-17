using System;
using System.Windows.Forms;

namespace RezumiChanGUI
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private async void generateButton_Click(object sender, EventArgs e)
        {
            generateButton.Enabled = false;
            progressBar.Value = 0;
            statusLabel.Text = "Starting…";

            var progress = new Progress<RezumiChanCLI.Program.PipelineProgress>(p =>
            {
                statusLabel.Text = p.Message;
                progressBar.Value = Math.Clamp(p.Percent, 0, 100);
            });

            const int maxAttempts = 5;
            int attempt = 0;

            while (attempt < maxAttempts)
            {
                try
                {
                    attempt++;

                    statusLabel.Text = $"Attempt {attempt} of {maxAttempts}…";
                    progressBar.Value = 0;

                    await RezumiChanCLI.Program.RunResumePipeline(
                        jobPostingTextBox.Text,
                        progress
                    );

                    statusLabel.Text = "Success!";
                    break; // Exit loop if successful
                }
                catch (Exception ex)
                {
                    if (attempt >= maxAttempts)
                    {
                        MessageBox.Show(
                            $"Failed after {maxAttempts} attempts.\n\n{ex.Message}",
                            "Error"
                        );
                        statusLabel.Text = "Failed.";
                        break;
                    }

                    statusLabel.Text = $"Error. Retrying ({attempt}/{maxAttempts})…";
                    await Task.Delay(1500); // short delay before retry
                }
            }

            generateButton.Enabled = true;
        }


    }
}