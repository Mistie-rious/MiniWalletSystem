"use client";

import { Button } from "@/components/ui/button";
import { useToast } from "@/hooks/use-toast"


export default function ToastTest() {
    const { toast } = useToast()
  const showSuccessToast = () => {
    console.log("Showing success toast");
    toast({
      title: "Success",
      description: "This is a success toast message",
    });
  };

  const showErrorToast = () => {
    console.log("Showing error toast");
    toast({
      variant: "destructive",
      title: "Error",
      description: "This is an error toast message",
    });
  };

  return (
    <div className="p-8 flex flex-col gap-4 items-center">
      <h1 className="text-2xl font-bold">Toast Test</h1>
      <Button onClick={showSuccessToast}>Show Success Toast</Button>
      <Button variant="destructive" onClick={showErrorToast}>Show Error Toast</Button>
    </div>
  );
}