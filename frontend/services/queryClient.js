// api/queryClient.js or api/queryClient.ts
import { QueryClient } from '@tanstack/react-query';
import axios from 'axios';

// Create a custom query client with default settings
 const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        // Don't retry on 401 errors since that indicates auth problems
        if (axios.isAxiosError(error) && error.response?.status === 401) {
          return false;
        }
        // Otherwise retry once (or adjust as needed)
        return failureCount < 1;
      },
      staleTime: 1000 * 60 * 10, // 10 minutes
    },
  },
});

// Add global error handler for auth errors
queryClient.setDefaultOptions({
  mutations: {
    onError: (error) => handleQueryError(error),
  },
  queries: {
    onError: (error) => handleQueryError(error),
  },
});

// Error handler for authentication errors
function handleQueryError(error) {
  if (axios.isAxiosError(error) && error.response?.status === 401) {
    // Clear auth data on 401 errors
    sessionStorage.removeItem('jwtToken');
    sessionStorage.removeItem('userId');
    sessionStorage.removeItem('userEmail');
    
    // If not on login page, redirect to login
    if (typeof window !== 'undefined' && 
        !window.location.pathname.includes('/login')) {
      window.location.href = '/login';
    }
  }
}


export default queryClient;
