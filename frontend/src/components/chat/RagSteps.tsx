import * as React from "react";
import { motion, AnimatePresence } from "framer-motion";
import { CaretDown, Database, Code, Lightning, MagnifyingGlass } from "@phosphor-icons/react";
import { cn } from "@/lib/utils";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { TerminalCodeBlock } from "./TerminalCodeBlock";
import { type RagStep } from "@/lib/chat-service";

interface RagStepsProps {
  steps: RagStep[];
}

export const RagSteps: React.FC<RagStepsProps> = ({ steps }) => {
  const [isOpen, setIsOpen] = React.useState(false);

  const getIcon = (title: string) => {
    const t = title.toLowerCase();
    if (t.includes("vector") || t.includes("embedding")) return <Lightning size={16} />;
    if (t.includes("schema") || t.includes("retrieve")) return <MagnifyingGlass size={16} />;
    if (t.includes("sql") && t.includes("gen")) return <Code size={16} />;
    if (t.includes("sql") && t.includes("exec")) return <Database size={16} />;
    return <Lightning size={16} />;
  };

  return (
    <div className="mt-4 mb-2">
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-primary/5 hover:bg-primary/10 text-primary/60 hover:text-primary transition-all text-xs font-semibold border border-primary/10"
      >
        <Lightning weight="fill" className={cn("transition-transform duration-500", isOpen && "animate-pulse")} />
        <span>RAG TRACE ({steps.length} steps)</span>
        <CaretDown size={14} className={cn("transition-transform duration-300", isOpen && "rotate-180")} />
      </button>

      <AnimatePresence>
        {isOpen && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.4, ease: [0.23, 1, 0.32, 1] }}
            className="overflow-hidden"
          >
            <div className="pt-4 space-y-4">
              {steps.map((step, idx) => (
                <div key={idx} className="relative pl-6 pb-2 border-l border-primary/20 last:border-l-transparent">
                  <div className="absolute left-[-9px] top-0 w-4 h-4 rounded-full bg-background border-2 border-primary flex items-center justify-center text-primary">
                    <div className="w-1.5 h-1.5 rounded-full bg-primary" />
                  </div>
                  
                  <div className="flex flex-col gap-2 glass-panel border border-primary/5 p-4 rounded-xl">
                    <div className="flex items-center gap-2 text-xs font-bold text-primary/80 uppercase tracking-wider">
                      {getIcon(step.title)}
                      {step.title}
                    </div>
                    
                    <div className="text-sm text-foreground/70 leading-relaxed markdown-content max-w-full overflow-x-auto">
                      <ReactMarkdown 
                        remarkPlugins={[remarkGfm]}
                        components={{
                          pre({ children }) {
                            return <>{children}</>;
                          },
                          code({ node, className, children, ...props }) {
                            const match = /language-(\w+)/.exec(className || "");
                            const isInline = !className;
                            
                            if (!isInline) {
                              return (
                                <TerminalCodeBlock 
                                  language={match?.[1]} 
                                  value={String(children).replace(/\n$/, "")} 
                                />
                              );
                            }

                            return (
                              <code className={cn("px-1 py-0.5 rounded bg-primary/5 text-primary font-mono text-[0.85em]", className)} {...props}>
                                {children}
                              </code>
                            );
                          }
                        }}
                      >
                        {step.content}
                      </ReactMarkdown>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
};
