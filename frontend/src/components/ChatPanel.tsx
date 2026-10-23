import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { api, ApiError, type ChatMessage } from "../api";
import { Button, Notice } from "./ui";

/**
 * Local assistant popup in the bottom-right corner. The conversation lives only in this
 * component's memory and is sent with each question; it clears on New chat, page change, or refresh.
 */
export function ChatPanel() {
  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [draft, setDraft] = useState("");
  const [replying, setReplying] = useState(false);
  const [error, setError] = useState<string>();
  const requestRef = useRef<AbortController | null>(null);
  const logRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const launcherRef = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(false);

  // Stop any reply in progress when the page closes.
  useEffect(() => () => requestRef.current?.abort(), []);

  useEffect(() => {
    if (open) inputRef.current?.focus();
    else if (wasOpen.current) launcherRef.current?.focus();
    wasOpen.current = open;
  }, [open]);

  useEffect(() => {
    const log = logRef.current;
    if (log) log.scrollTop = log.scrollHeight;
  }, [messages, open]);

  async function handleSend(e?: FormEvent) {
    e?.preventDefault();
    const question = draft.trim();
    if (!question || requestRef.current) return;

    const before = messages;
    const history: ChatMessage[] = [...before, { role: "user", content: question }];
    const controller = new AbortController();
    requestRef.current = controller;
    setMessages([...history, { role: "assistant", content: "" }]);
    setDraft("");
    setError(undefined);
    setReplying(true);

    let received = false;
    try {
      await api.streamChat(
        history,
        (text) => {
          if (controller.signal.aborted) return;
          received = true;
          setMessages((current) => {
            const last = current[current.length - 1];
            return [...current.slice(0, -1), { ...last, content: last.content + text }];
          });
        },
        controller.signal,
      );
    } catch (err) {
      if (controller.signal.aborted) return;
      const reason = err instanceof ApiError ? err.message : "Something went wrong. Please try again.";
      if (received) {
        setError(`The reply stopped before it finished. ${reason}`);
      } else {
        // Nothing came back: return the question to the input so it can be sent again.
        setMessages(before);
        setDraft(question);
        setError(reason);
      }
    } finally {
      if (requestRef.current === controller) {
        requestRef.current = null;
        setReplying(false);
      }
    }
  }

  function handleNewChat() {
    requestRef.current?.abort();
    requestRef.current = null;
    setReplying(false);
    setMessages([]);
    setError(undefined);
    inputRef.current?.focus();
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      void handleSend();
    }
  }

  if (!open) {
    return (
      <Button ref={launcherRef} variant="primary" className="chat-launcher" aria-expanded={false} onClick={() => setOpen(true)}>
        {replying ? "Assistant (replying…)" : "Assistant"}
      </Button>
    );
  }

  return (
    <section
      className="chat"
      role="dialog"
      aria-labelledby="chat-title"
      onKeyDown={(e) => {
        if (e.key === "Escape") setOpen(false);
      }}
    >
      <div className="chat__header">
        <h2 id="chat-title">Assistant</h2>
        <div className="chat__header-actions">
          <Button variant="text" onClick={handleNewChat} disabled={messages.length === 0 && !error}>
            New chat
          </Button>
          <Button variant="text" aria-expanded={true} onClick={() => setOpen(false)}>
            Close
          </Button>
        </div>
      </div>

      <div ref={logRef} className="chat__log" role="log" aria-label="Conversation" aria-live="polite" aria-busy={replying}>
        {messages.length === 0 ? (
          <p className="chat__empty">
            Ask a question, including about your saved expenses or policy documents, for example "What did we spend on
            Security last month?" or "Is lift rope replacement covered by the AMC?"
            Answers come from the local model on this Mac, and it can only read expenses, not change them.
            The chat isn't saved and clears when you refresh or leave this page.
          </p>
        ) : (
          messages.map((m, i) => {
            const pending = replying && i === messages.length - 1;
            return (
              <div key={i} className={`chat__message chat__message--${m.role}`}>
                <span className="chat__author">
                  {m.role === "user" ? "You" : "Assistant"}
                  {pending && <span className="chat__replying"> · Replying…</span>}
                </span>
                {m.content && <p className="chat__text">{m.content}</p>}
              </div>
            );
          })
        )}
      </div>

      <form className="chat__form" onSubmit={handleSend}>
        {error && <Notice tone="error">{error}</Notice>}
        <label className="field__label" htmlFor="chat-input">
          Your question
        </label>
        <textarea
          ref={inputRef}
          id="chat-input"
          className="input chat__input"
          rows={2}
          maxLength={4000}
          aria-describedby="chat-input-hint"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={handleKeyDown}
        />
        <div className="chat__actions">
          <span id="chat-input-hint" className="field__hint">
            Enter to send · Shift+Enter for a new line
          </span>
          <Button type="submit" variant="primary" disabled={replying || !draft.trim()}>
            {replying ? "Replying…" : "Send"}
          </Button>
        </div>
      </form>
    </section>
  );
}
