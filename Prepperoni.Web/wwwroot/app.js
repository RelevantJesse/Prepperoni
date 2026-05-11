const form = document.querySelector("#question-form");
const input = document.querySelector("#job-title");
const button = document.querySelector("#submit-button");
const statusBox = document.querySelector("#status");
const results = document.querySelector("#results");

form.addEventListener("submit", async (event) => {
  event.preventDefault();

  const jobTitle = input.value.trim();
  if (!jobTitle) {
    showStatus("Add a job title first. Even startups need nouns.", true);
    return;
  }

  setLoading(true);
  showStatus("Warming up the tiny interview oven...");
  results.replaceChildren();

  try {
    const response = await fetch("/api/interview-questions", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({ jobTitle })
    });

    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.message || problem?.detail || "The question oven coughed. Try again.");
    }

    const data = await response.json();
    renderQuestions(data.questions);
    showStatus(`Fresh out of ${data.model}. No buzzword garnish required.`);
  } catch (error) {
    showStatus(error.message, true);
  } finally {
    setLoading(false);
  }
});

function renderQuestions(questions) {
  const cards = questions.map((item, index) => {
    const card = document.createElement("article");
    card.className = "question-card";

    const heading = document.createElement("h2");
    const number = document.createElement("span");
    number.className = "number";
    number.textContent = `${index + 1}.`;

    const question = document.createElement("span");
    question.textContent = item.question;

    const why = document.createElement("p");
    why.textContent = item.why;

    heading.append(number, question);
    card.append(heading, why);
    return card;
  });

  results.replaceChildren(...cards);
}

function setLoading(isLoading) {
  button.disabled = isLoading;
  input.disabled = isLoading;
  button.textContent = isLoading ? "Baking..." : "Bake questions";
}

function showStatus(message, isError = false) {
  statusBox.textContent = message;
  statusBox.classList.toggle("error", isError);
}
