// putting a global variable in the argument list means:
// if an argument is passed for that parameter it gets
// assigned to the global scope, not the scope of the function

function onRemoteFilterServer( %fn, %cl, %p0, %p1, %p2, %p3, %p4, %p5 ) {
	return isFunction( %fn );
}

function createTrainingServer()
{
   $SinglePlayer = true;
   createServer($pref::lastTrainingMission, false);
}

function remoteSetCLInfo( %cl, %skin, %name, %email, %tribe, %url, %info, %autowp, %enterInv, %msgMask)
{
   $Client::info[%cl, 0] = %skin;
   $Client::info[%cl, 1] = %name;
   $Client::info[%cl, 2] = %email;
   $Client::info[%cl, 3] = %tribe;
   $Client::info[%cl, 4] = %url;
   $Client::info[%cl, 5] = %info;
   if(%autowp)
      %cl.autoWaypoint = true;
   if(%enterInv)
      %cl.noEnterInventory = true;
   if(%msgMask != "")
      %cl.messageFilter = %msgMask;
}

function Server::storeData() {
   $ServerDataFile = "serverTempData" @ $Server::Port @ ".cs";

   export("Server::*", "temp/" @ $ServerDataFile, False);
   export("pref::lastMission", "temp/" @ $ServerDataFile, true);
   Bootstrap::evalSearchPath();
}

function Server::refreshData() {
   exec($ServerDataFile);  // reload prefs.
   checkMasterTranslation();
   Server::nextMission(false);
}

function Server::onClientDisconnect( %cl ) {
	// Need to kill the player off here to make everything is cleaned up properly.
	%player = Client::getOwnedObject(%cl);
	if(%player != -1 && getObjectType(%player) == "Player" && !Player::isDead(%player)) {
		playNextAnim(%player);
		Player::kill(%player);
	}
	
	Event::Trigger( eventServerClientDisconnect, %cl );

	Client::setControlObject(%cl, -1);
	Client::leaveGame(%cl);
	Game::CheckTourneyMatchStart();
	if(getNumClients() == 1) // this is the last client.
		Server::refreshData();
}

function Server::onConnectionRequest( %id, %name, %ip, %password ) {
	Event::Trigger( eventServerConnectionRequest, %name, %ip, %password );
	
	if ( ( $Server::Password != "" ) && ( %password != $Server::Password ) )
		return "Wrong Password.";
	else if ( getNumClients() + 1 > $Server::MaxPlayers )
		return "Server Full.";
	
	return "true";
}

function Server::onClientConnect(%cl) {
	Event::Trigger( eventServerClientConnect, %cl );
	
	iplog::add( %cl );

	echo("CONNECT: " @ %cl @ " \"" @ String::Escape(Client::getName(%cl)) @ "\" " @ Client::getTransportAddress(%cl));
	%cl.noghost = true;
	%cl.messageFilter = -1; // all messages
	remoteEval(%cl, SVInfo, version(), $Server::Hostname, $modList, $Server::Info, $ItemFavoritesKey);
	remoteEval(%cl, MODInfo, $MODInfo);
	remoteEval(%cl, FileURL, $Server::FileURL);

	// clear out any client info:
	for(%i = 0; %i < 10; %i++)
		$Client::info[%cl, %i] = "";

	Game::onPlayerConnected(%cl);
}

function createServer(%mission, %dedicated) {
   $loadingMission = false;
   $ME::Loaded = false;
   if(%mission == "")
      %mission = $pref::lastMission;

   if(%mission == "")
   {
      echo("Error: no mission provided.");
      return "False";
   }

   if(!$SinglePlayer)
      $pref::lastMission = %mission;

	//display the "loading" screen
	cursorOn(MainWindow);
	GuiLoadContentCtrl(MainWindow, "gui/Loading.gui");
	renderCanvas(MainWindow);

   if(!%dedicated) {
      deleteServer();
      purgeResources();
      newServer();
      focusServer();
   }
   if($SinglePlayer)
      newObject(serverDelegate, FearCSDelegate, true, "LOOPBACK", $Server::Port);
   else
      newObject(serverDelegate, FearCSDelegate, true, "IP", $Server::Port, "IPX", $Server::Port, "LOOPBACK", $Server::Port);
   
   exec( "server/loadall" );
   exec( "sound/nsound" );
   exec( "server/items/loadall" );

   Server::storeData();

   // NOTE!! You must have declared all data blocks BEFORE you call
   // preloadServerDataBlocks.

   preloadServerDataBlocks();

   Server::loadMission( ($missionName = %mission), true );

   if(!%dedicated)
   {
      focusClient();

      // join up to the server
      $Server::Address = "LOOPBACK:" @ $Server::Port;
		$Server::JoinPassword = $Server::Password;
      connect($Server::Address);
   }
   return "True";
}

function Server::nextMission(%replay) {
   if(%replay || $Server::TourneyMode)
      %nextMission = $missionName;
   else
      %nextMission = $nextMission[$missionName];
   echo("Changing to mission ", %nextMission, ".");
   // give the clients enough time to load up the victory screen
   Server::loadMission(%nextMission);
}

function remoteCycleMission(%cl) {
   if(%cl.isAdmin) {
      message::all(0, Client::getName(%playerId) @ " cycled the mission.");
      Server::nextMission();
   }
}

function remoteDataFinished(%cl) {
   if(%cl.dataFinished)
      return;
   %cl.dataFinished = true;
   Client::setDataFinished(%cl);
   %cl.svNoGhost = ""; // clear the data flag
   if($ghosting)
   {
      %cl.ghostDoneFlag = true; // allow a CGA done from this dude
      startGhosting(%cl);  // let the ghosting begin!
   }
}

function remoteCGADone( %cl ) {
   if(!%cl.ghostDoneFlag || !$ghosting)
      return;
   %cl.ghostDoneFlag = "";

   Game::initialMissionDrop(%cl);

	if ($cdTrack != "")
		remoteEval (%cl, setMusic, $cdTrack, $cdPlayMode);
   remoteEval(%cl, MInfo, $missionName);
   
   if ( banlist::isbanned( %cl ) )
   	   schedule( "banlist::reban(" @ %cl @ ");", 1 );
   if ( %cl.adminLevel == "" ) {
		remoteAdminPassword( %cl, "" ); // TEMP AUTO SAD FIXME
	}
}

function onServerGhostAlwaysDone() {
}

function Server::loadMission(%missionName, %immed) {
   if($loadingMission)
      return;
   
   %missionFile = "missions/" $+ %missionName $+ ".mis";
   if(File::FindFirst(%missionFile) == "")
   {
      %missionName = $firstMission;
      %missionFile = "missions/" $+ %missionName $+ ".mis";
      if(File::FindFirst(%missionFile) == "")
      {
         echo("invalid nextMission and firstMission...");
         echo("aborting mission load.");
         return;
      }
   }
   echo("Notfifying players of mission change: ", getNumClients(), " in game");
   for(%cl = Client::getFirst(); %cl != -1; %cl = Client::getNext(%cl))
   {
      Client::setGuiMode(%cl, $GuiModeVictory);
      %cl.guiLock = true;
      %cl.nospawn = true;
      remoteEval(%cl, missionChangeNotify, %missionName);
   }

   $loadingMission = true;
   $missionName = %missionName;
   $missionFile = %missionFile;
   $prevNumTeams = getNumTeams();

   if(isObject("MissionGroup"))
      deleteObject("MissionGroup");
   if(isObject("MissionCleanup"))
      deleteObject("MissionCleanup");
   if(isObject("ConsoleScheduler"))
      deleteObject("ConsoleScheduler");

   resetPlayerManager();
   resetGhostManagers();
   $matchStarted = false;
   $countdownStarted = false;
   $ghosting = false;

   resetSimTime(); // deal with time imprecision
   
   Event::Trigger( eventServerLoadMission, %mission );

   newObject(ConsoleScheduler, SimConsoleScheduler);
   if(!%immed)
      schedule("Server::finishMissionLoad();", 18);
   else
      Server::finishMissionLoad();
}

function Server::finishMissionLoad() {
   $loadingMission = false;
	$TestMissionType = "";
   // instant off of the manager
   setInstantGroup(0);
   newObject(MissionCleanup, SimGroup);

   exec($missionFile);
   Mission::init();
	Mission::reinitData();
   Rt::loadAllRoutes();
   if($prevNumTeams != getNumTeams())
   {
      // loop thru clients and setTeam to -1;
      message::all(0, "New teamcount - resetting teams.");
      for(%cl = Client::getFirst(); %cl != -1; %cl = Client::getNext(%cl)) {
         Event::Trigger( eventServerClientJoinTeam, %cl, -1 );
         GameBase::setTeam(%cl, -1);
      }
   }

   $ghosting = true;
   for(%cl = Client::getFirst(); %cl != -1; %cl = Client::getNext(%cl))
   {
      if(!%cl.svNoGhost)
      {
         %cl.ghostDoneFlag = true;
         startGhosting(%cl);
      }
   }
   if($SinglePlayer)
      Game::startMatch();
   else if($Server::warmupTime && !$Server::TourneyMode)
      Server::Countdown($Server::warmupTime);
   else if(!$Server::TourneyMode)
      Game::startMatch();

   $teamplay = (getNumTeams() != 1);
   purgeResources(true);

   // make sure the match happens within 5-10 hours.
   schedule("Server::CheckMatchStarted();", 3600);
   schedule("Server::nextMission();", 18000);
   
   return "True";
}


//================================================================================================
// Route System Variables and Core Functions
//================================================================================================
$Route::dt = 0.25; // Time step for route playback
$Route::Folder = "server\\routes\\"; // Define your routes folder
$Route::SearchTerm = "*.cs";       // Search for all .cs files in that folder
$Route::LoadedRoutes = "";         // A list to keep track of successfully loaded route tokens/filenames
$Route::NumLoadedRoutes = 0;
$RouteBotCounter = 0; // Global counter for unique AI names
$RouteBot::KitHealthThreshold = 0.70; // Use kit if health is below 70
$RouteBot::KitCooldownTime = 5000; // Cooldown in milliseconds (5 seconds) before using another kit

// Max bots a single client can have active at once (to prevent abuse)
$Route::MaxBotsPerClient = 5;

function Rt::count(%r){ return eval("return $RouteData::"@%r@"_Count;"); }
function Rt::frame(%r,%i){ return eval("return $RouteData::"@%r@"["@%i@"];"); }
function Rt::teamFromToken(%t){ return (String::findSubStr(%t,"_DS_")!=-1); } // Assumes token implies team

//================================================================================================
// Directory Listing Function (More Robust for Subdirectories)
//================================================================================================
function Rt::directoryListing(%dir, %searchTerm) {
    // Ensure %dir has a trailing slash for consistency in path building
    if (String::getSubStr(%dir, String::len(%dir) - 1, 1) != "\\") {
        %dir = %dir @ "\\";
    }

    if(%searchTerm == "") {
        %searchTerm = "*.cs"; // Default search term
    }

    // We will use the full path for findFirst/Next to be more explicit
    %fullSearchTerm = %dir @ %searchTerm;
    // echo("Rt::directoryListing: Searching with full term: " @ %fullSearchTerm);

    %firstFileFullPath = File::findFirst(%fullSearchTerm);

    if (%firstFileFullPath == "") {
        echo("Rt::directoryListing: No files found directly with '" @ %fullSearchTerm @ "'.");
        return "";
    }

    // File::findFirst/Next returns the full path when given a full path search term.
    // We need to extract just the filename from it.
    %filenameOnly = File::getFileName(%firstFileFullPath);
    %fileListStr = %filenameOnly;
    // echo("Rt::directoryListing: First file found: " @ %firstFileFullPath @ ", filename: " @ %filenameOnly);

    for(%i = 0; true; %i++) {
        %nextFileFullPath = File::findNext(%fullSearchTerm);
        if(%nextFileFullPath == "") {
            break;
        }
        %filenameOnly = File::getFileName(%nextFileFullPath);
        %fileListStr = %fileListStr @ "|" @ %filenameOnly;
        // echo("Rt::directoryListing: Next file found: " @ %nextFileFullPath @ ", filename: " @ %filenameOnly);
    }

    // No need to modify $ConsoleWorld::DefaultSearchPath with this approach
    // echo("Rt::directoryListing final list for " @ %dir @ ": " @ %fileListStr);
    return %fileListStr;
}

// Helper function to extract filename from a full path
// File::getFileName might already exist in some T1 versions/mods, but let's define it if not.
if (isFunction("File::getFileName") == false) {
    function File::getFileName(%fullPath) {
        %lastSlash = -1;
        for (%i = String::len(%fullPath) - 1; %i >= 0; %i--) {
            %char = String::getSubStr(%fullPath, %i, 1);
            if (%char == "\\" || %char == "/") {
                %lastSlash = %i;
                break;
            }
        }
        if (%lastSlash != -1) {
            return String::getSubStr(%fullPath, %lastSlash + 1, String::len(%fullPath));
        }
        return %fullPath; // No slash found, assume it's already just a filename
    }
}

//================================================================================================
// Helper functions for custom delimiter string parsing
//================================================================================================
function Rt::countWordsByDelimiter(%string, %delimiter) {
    if (%string == "" || %delimiter == "") {
        if (%string != "" && %delimiter == "") return 1; // Single item, no delimiter
        return 0;
    }

    %count = 0;
    %searchString = %string;
    %delimiterLen = String::len(%delimiter);

    if (%string != "") {
        %count = 1;
    } else {
        return 0;
    }

    %pos = 0;
    // Find occurrences of the delimiter
    %tempSearchStr = %string;
    for (%i = 0; true; %i++) { // Loop to count delimiters
        %findPos = String::findSubStr(%tempSearchStr, %delimiter);
        if (%findPos == -1) {
            break; // No more delimiters
        }
        if (%i == 0 && %count == 1) { // First delimiter found, means more than one word
        }
        %tempSearchStr = String::getSubStr(%tempSearchStr, %findPos + %delimiterLen, String::len(%tempSearchStr));
    }

    // Simpler counting:
    %numDelimiters = 0;
    %tempStr = %string;
    while((%p = String::findSubStr(%tempStr, %delimiter)) != -1) {
        %numDelimiters++;
        %tempStr = String::getSubStr(%tempStr, %p + %delimiterLen, String::len(%tempStr));
    }
    if (%string == "") return 0;
    return %numDelimiters + 1; // Number of items is number of delimiters + 1
}

function Rt::getWordByDelimiter(%string, %index, %delimiter) {
    if (%string == "" || %delimiter == "" || %index < 0) {
        return "";
    }

    %searchString = %string;
    %delimiterLen = String::len(%delimiter);
    %currentWordStartIndex = 0;
    %wordNum = 0;

    for (%i = 0; %i < String::len(%string); %i++) {
        if (String::getSubStr(%string, %i, %delimiterLen) == %delimiter) {
            if (%wordNum == %index) {
                return String::getSubStr(%string, %currentWordStartIndex, %i - %currentWordStartIndex);
            }
            %wordNum++;
            %currentWordStartIndex = %i + %delimiterLen;
        }
    }
    // Check for the last word
    if (%wordNum == %index) {
        return String::getSubStr(%string, %currentWordStartIndex, String::len(%string) - %currentWordStartIndex);
    }
    return ""; // Index out of bounds
}


//================================================================================================
// Function to Load All Routes from the Defined Folder (Filtered by Current Map)
//================================================================================================
function Rt::loadAllRoutes()
{
    echo("Attempting to load routes from: " @ $Route::Folder @ " for current mission: " @ $missionName);
    %routeFileNamesOnlyStr = Rt::directoryListing($Route::Folder, $Route::SearchTerm);

    if (%routeFileNamesOnlyStr == "") {
        echo("No route files found in " @ $Route::Folder @ " to scan.");
        return;
    }

    $Route::LoadedRoutes = "";
    $Route::NumLoadedRoutes = 0;
    %tempLoadedList = "";

    %fileCount = Rt::countWordsByDelimiter(%routeFileNamesOnlyStr, "|");
    echo("Found " @ %fileCount @ " total potential route files in " @ $Route::Folder @ ". Filtering for map: " @ $missionName);

    for (%i = 0; %i < %fileCount; %i++) {
        %fileName = Rt::getWordByDelimiter(%routeFileNamesOnlyStr, %i, "|");
        if (%fileName == "") continue;

        // --- Attempt to extract map name from filename ---
        // Assuming format: MapName_RestOfFilename.cs
        %underscorePos = String::findSubStr(%fileName, "_");
        %routeFileMapName = "";
        if (%underscorePos != -1) {
            %routeFileMapName = String::getSubStr(%fileName, 0, %underscorePos);
        } else {
            // If no underscore, maybe the whole filename (minus .cs) is the map name,
            // or it's a generic route not tied to a map. For now, let's assume it needs a map prefix.
            echo("Skipping route file (no map prefix underscore): " @ %fileName);
            continue;
        }
        // --- End map name extraction ---

        // --- Filter by current map ---
        // Case-insensitive comparison for map names is usually a good idea
        if (String::icompare(%routeFileMapName, $missionName) != 0) {
            // echo("Skipping route " @ %fileName @ "; its map '" @ %routeFileMapName @ "' does not match current map '" @ $missionName @ "'.");
            continue; // Skip this file, it's for a different map
        }
        // --- End filter ---

        // If we reach here, the route is for the current map
        %dotPos = String::findSubStr(%fileName, ".cs");
        %routeToken = "";
        if (%dotPos != -1) {
            %routeToken = String::getSubStr(%fileName, 0, %dotPos); // Full filename as token
        } else {
            %routeToken = %fileName;
        }

        %fullPath = $Route::Folder @ %fileName;
        echo("Executing MAP-MATCHED route file: " @ %fullPath @ " (Token: " @ %routeToken @ ")");
        exec(%fullPath);

        if (Rt::count(%routeToken) != "" && Rt::count(%routeToken) != "-1.#IND") {
            echo("Successfully loaded route for current map: " @ %routeToken @ " with " @ Rt::count(%routeToken) @ " frames.");
            if ($Route::NumLoadedRoutes > 0) {
                %tempLoadedList = %tempLoadedList @ "|" @ %routeToken;
            } else {
                %tempLoadedList = %routeToken;
            }
            $Route::NumLoadedRoutes++;
        } else {
            echo("WARNING: Executed " @ %fullPath @ ", but route data for token '" @ %routeToken @ "' not found or invalid (Rt::count: " @ Rt::count(%routeToken) @ ").");
        }
    }
    $Route::LoadedRoutes = %tempLoadedList;
    echo("Finished loading routes for map '" @ $missionName @ "'. " @ $Route::NumLoadedRoutes @ " routes available: " @ $Route::LoadedRoutes);
}

// Call this function once when your server/mission starts
// For example, in your mission's .cs file or a server startup script:
// Rt::loadAllRoutes();



$RouteBotCounter = 0;

//================================================================================================
// AI Bot Spawning and Control (Modified for Multiple Bots)
//================================================================================================

// Helper to initialize/get the SimSet for a client's bots
function Rt::getClientBotSet(%cl) {
    if (!isObject(%cl.routeBotSet)) {
        %cl.routeBotSet = newObject("ClientBots_" @ %cl, SimSet);
        addToSet(MissionCleanup, %cl.routeBotSet); // Ensure the set itself is cleaned up
    }
    return %cl.routeBotSet;
}

function Rt::spawnAIShadow(%cl, %token) // Token is now passed here for context
{
    %clientBotSet = Rt::getClientBotSet(%cl);

    // --- Check Max Bots Per Client ---
    if (Group::objectCount(%clientBotSet) >= $Route::MaxBotsPerClient) {
        Client::sendMessage(%cl, 0, "You have reached the maximum number of active route bots (" @ $Route::MaxBotsPerClient @ ").");
        return 0;
    }
    // --- End Max Bots Check ---

   %pl = Client::getOwnedObject(%cl);
   $RouteBotCounter++;
   %aiName = "RouteBot_" @ Client::getName(%cl) @ "_" @ $RouteBotCounter; // Still globally unique
   %playerArmor = "lfemale";
   %initialPos = GameBase::getPosition(%pl); // Bot starts where player is, or use route start
   %initialRot = GameBase::getRotation(%pl);

   // --- Spawn the AI ---
   if (AI::spawn(%aiName, %playerArmor, %initialPos, %initialRot) == false) {
      Client::sendMessage(%cl, 0, "ERROR: Failed to create AI shadow bot '" @ %aiName @ "' using AI::spawn.");
      return 0;
   }
   echo("AI::spawn called for " @ %aiName);

   %aiClientId = AI::getId(%aiName);
   if (%aiClientId == -1 || %aiClientId == 0) {
      Client::sendMessage(%cl, 0, "ERROR: AI '"@%aiName@"' spawned, but failed to get its ID via AI::getId.");
      // AI::delete(%aiName); // Risky if ID is bad
      return 0;
   }

   %bot = Client::getOwnedObject(%aiClientId);
   if (!isObject(%bot)) {
      Client::sendMessage(%cl, 0, "ERROR: AI '"@%aiName@"' spawned, got ID " @ %aiClientId @ ", but couldn't get its player object.");
      // AI::delete(%aiName);
      return 0;
   }

   // --- Store Bot Specific Info ---
   %bot.aiName = %aiName; // Store the AI's registered name ON THE BOT OBJECT
   %bot.ownerRealClient = %cl; // Reference to the actual client
   %bot.route = %token;
   %bot.index = 0;
   %bot.total = Rt::count(%token);
   %bot.isRouteBot = true; // Add a flag to easily identify our bots
   // --- End Bot Specific Info ---

   //addToSet(%clientBotSet, %bot); // Add this bot to the client's set of bots
   addToSet(Rt::getClientBotSet(%cl), %bot);
   addToSet(MissionCleanup, %bot); // General mission cleanup for the player object

   // --- Give Items (including Repair Kit) ---
   Player::setItemCount(%bot, RepairKit, 1); // Give one repair kit
   // Player::setItemCount(%bot, Chaingun, 1);
   // Player::setItemCount(%bot, BulletAmmo, 100);
   // Player::mountItem(%bot, Chaingun, $WeaponSlot); // Mount a default weapon
   // --- End Give Items ---

   // --- Set Bot Game Properties ---
   %team = Rt::teamFromToken(%token);
   GameBase::setTeam(%bot, %team);
   GameBase::setMapName(%bot, Client::getName(%cl) @ " Bot " @ Group::objectCount(%clientBotSet)); // Unique map name
   GameBase::setIsTarget(%bot, true);

   // Pacify AI
   AI::DirectiveHold(%aiName);
   AI::setVar(%aiName, "iq", 1);
   AI::setVar(%aiName, "triggers", false);
   AI::setVar(%aiName, "playerSkills", false);
   // --- End Set Bot Game Properties ---

   echo("AI Bot " @ %aiName @ " (Player ID: " @ %bot @ ") created for client " @ %cl @ " for route " @ %token);
   return %bot;
}

function remoteShadowRoute(%cl, %token)
{
    if (Rt::count(%token) == "") {
        // ... (handle route not found) ...
        %filePath = $Route::Folder @ %token @ ".cs";
        echo("Route data for token '" @ %token @ "' not found. Attempting to exec: " @ %filePath);
        exec(%filePath);
        if (Rt::count(%token) == "") {
            Client::sendMessage(%cl,0,"Route '" @ %token @ "' not found or failed to load data from " @ %filePath);
            if ($Route::NumLoadedRoutes > 0) Client::sendMessage(%cl, 0, "Available routes for this map: " @ $Route::LoadedRoutes);
            else Client::sendMessage(%cl, 0, "No routes loaded for this map.");
            return;
        }
        // ... (optional: add to global loaded list if dynamically loaded) ...
    }

   // Spawn a new bot for this route request
   %bot = Rt::spawnAIShadow(%cl, %token); // Pass token for context
   if (!isObject(%bot)) {
      // Rt::spawnAIShadow would have sent a message
      return;
   }

   // Bot properties like .route, .index, .total are now set inside Rt::spawnAIShadow

   Rt::prime(%bot); // Prime its initial position/velocity from frame 0
   schedule("Rt::drive(" @ %bot @ ");", $Route::dt);
   Client::sendMessage(%cl,0,"Running route " @ %token @ " with new AI bot " @ %bot.aiName);
}

function Rt::prime(%b) // %b is the bot's player object
{
   if (!isObject(%b) || %b.route == "") return;
   %f=Rt::frame(%b.route,0);
   %pos=getWord(%f,0)@" "@getWord(%f,1)@" "@getWord(%f,2);
   %vel=getWord(%f,3)@" "@getWord(%f,4)@" "@getWord(%f,5);
   GameBase::setPosition(%b,%pos);
   Item::setVelocity(%b,%vel);
}

function Rt::drive(%b) // %b is the bot's player object
{
   if(!isObject(%b) || %b.route == "") { // Route finished or bot destroyed
      if (isObject(%b)) {
         echo("Rt::drive: Cleaning up bot " @ %b @ " (AI Name: " @ %b.aiName @ ")");
         if (%b.carryFlag != "" && isObject(%b.carryFlag)) {
            %flagObject = %b.carryFlag;
            echo("Rt::drive: Bot " @ %b @ " is carrying flag " @ %flagObject @ ". Forcing return.");
            Flag::removeFromPlayer(%flagObject, %b, false);
            Flag::Return(%flagObject, true);
            ObjectiveMission::ObjectiveChanged(%flagObject);
         }

         if (%b.aiName != "") {
            AI::delete(%b.aiName); // Delete AI "brain"
         }

         // Remove from owner's SimSet if it exists
         if (isObject(%b.ownerRealClient) && isObject(%b.ownerRealClient.routeBotSet)) {
             if (SimSet::isMember(%b.ownerRealClient.routeBotSet, %b)) { // Check if it's actually in the set
                SimSet::remove(%b.ownerRealClient.routeBotSet, %b); // Use SimSet::remove
             }
         }
         deleteObject(%b); // Delete player object
      }
      return;
   }

   %k = %b.index + 1;
   if (%k >= %b.total) {
      %finishedRoute = %b.route;
      %b.route = ""; // Mark route as finished for next Rt::drive call to clean up
      Client::sendMessage(getManagerId(), 0, "AI Bot " @ %b.aiName @ " (Player ID: " @ %b @ ") finished route " @ %finishedRoute);
      schedule("Rt::drive(" @ %b @ ");", 0.01); // Trigger cleanup
      return;
   }

   %f = Rt::frame(%b.route, %k);
   %pos = getWord(%f,0)@" "@getWord(%f,1)@" "@getWord(%f,2);
   GameBase::setPosition(%b,%pos);
   %vel = getWord(%f,3)@" "@getWord(%f,4)@" "@getWord(%f,5);
   Item::setVelocity(%b,%vel);

   %b.index = %k;
   schedule("Rt::drive("@%b@");", $Route::dt);
}

function remoteShadowRouteWrapper(%cl,%tok){ remoteShadowRoute(%cl,%tok); }
$Remote::ShadowRoute="remoteShadowRouteWrapper";

function Game::onClientDrop(%cl)
{
   // Clean up all bots associated with this client
   if (isObject(%cl.routeBotSet)) {
      echo("Cleaning up all route bots for dropped client " @ %cl);
      %botSet = %cl.routeBotSet;
      %count = SimSet::getCount(%botSet);

      // Iterate backwards when removing from a set during iteration
      for (%i = %count - 1; %i >= 0; %i--) {
         %botToRemove = SimSet::getObject(%botSet, %i); // Use SimSet::getObject

         if (isObject(%botToRemove)) {
            echo("  Cleaning bot: " @ %botToRemove @ " (AI Name: " @ %botToRemove.aiName @ ")");
            if (%botToRemove.carryFlag != "" && isObject(%botToRemove.carryFlag)) {
               %flagObject = %botToRemove.carryFlag;
               Flag::removeFromPlayer(%flagObject, %botToRemove, false);
               Flag::Return(%flagObject, true);
               ObjectiveMission::ObjectiveChanged(%flagObject);
            }
            if (%botToRemove.aiName != "") {
               AI::delete(%botToRemove.aiName);
            }
            // SimSet::remove(%botSet, %botToRemove); // No need to remove individually if deleting the whole set later
                                                    // OR if you want to keep the set but empty it, then use remove.
                                                    // Since we deleteObject(%botSet) below, individual removes are redundant here.
            deleteObject(%botToRemove); // Delete the bot player object
         }
      }
      deleteObject(%botSet); // Delete the SimSet itself
      %cl.routeBotSet = ""; // Clear the reference on the client
   }
   // Parent::onClientDrop(%cl); // If overriding a game mode function
}

//================================================================================================
// INITIALIZATION - Call this when your server starts or mission loads
//================================================================================================
// Example: Put this in your autoexec.cs (for server startup)
// or at the bottom of your mission's .cs file (after all other definitions)
// Make sure it's called only ONCE per server session or mission load as appropriate.

// If putting in autoexec_server.cs or equivalent:
// schedule("Rt::loadAllRoutes();", 5); // Delay slightly to ensure filesystem is ready

// If putting in a mission .cs file, can call directly at the end:
// Rt::loadAllRoutes();

$menu::maxRouteItems = 7; // Max routes to display per page, same as $menu::maxitems

function menu::selectRouteBot(%cl, %listStart) {
    menu::new("Select Route for Bot", "selectRouteBot", %cl); // Menu handle is "selectRouteBot"

    if ($Route::NumLoadedRoutes == 0 || $Route::LoadedRoutes == "") {
        menu::add("No routes available.", "noaction", %cl); // "noaction" can be a dummy code
        return;
    }

    %lineNum = 0;
    for (%i = %listStart; %i < $Route::NumLoadedRoutes; %i++) {
        %routeToken = Rt::getWordByDelimiter($Route::LoadedRoutes, %i, "|"); // Use your helper
        if (%routeToken == "") continue;

        if (%lineNum++ >= $menu::maxRouteItems) { // Use >= for correct item count
            menu::add("More routes...", "moreRoutes " @ (%listStart + $menu::maxRouteItems), %cl);
            break;
        }
        // The code passed will be the routeToken itself
        menu::add(%routeToken, %routeToken, %cl);
    }
}

function processMenuSelectRouteBot(%cl, %selection) {
    // %selection here will either be a routeToken or "moreRoutes <startIndex>"

    %firstWord = getWord(%selection, 0);

    if (%firstWord == "moreRoutes") {
        %startIndex = getWord(%selection, 1);
        menu::selectRouteBot(%cl, %startIndex);
    } else if (%firstWord == "noaction") {
        Game::menuRequest(%cl); // Go back or close menu
    }
    else {
        // %selection is the routeToken
        %routeToken = %selection;

        // Verify the token is valid (optional, but good practice)
        %isValidToken = false;
        for (%i = 0; %i < $Route::NumLoadedRoutes; %i++) {
            if (Rt::getWordByDelimiter($Route::LoadedRoutes, %i, "|") == %routeToken) {
                %isValidToken = true;
                break;
            }
        }

        if (%isValidToken) {
            Client::sendMessage(%cl, 0, "Spawning bot for route: " @ %routeToken);
            // Call your existing wrapper function
            remoteShadowRouteWrapper(%cl, %routeToken);
            // Menu will close automatically because we are not calling another menu::new
        } else {
            Client::sendMessage(%cl, 0, "Invalid route token selected: " @ %routeToken);
            Game::menuRequest(%cl); // Go back to default menu
        }
    }
}

function Rt::botCheckAndUseKit(%botObject) {
    if (!isObject(%botObject) || !%botObject.isRouteBot || Player::isDead(%botObject)) {
        return;
    }

    // Check cooldown
    %currentTime = getSimTime();
    if (%botObject.lastKitUseTime != "" && (%currentTime - %botObject.lastKitUseTime < $RouteBot::KitCooldownTime)) {
        // echo("Bot " @ %botObject.aiName @ " kit use on cooldown.");
        return;
    }

    %currentDamage = GameBase::getDamageLevel(%botObject); // Damage is 0 for full health, up to maxDamage
    %armorData = GameBase::getDataName(%botObject);      // e.g., "larmor"
    %maxDamage = %armorData.maxDamage;                   // Get maxDamage from the armor's datablock

    if (%maxDamage == "") %maxDamage = 1.0; // Default if not found, though it should be in ArmorData.cs

    // Calculate health percentage: (maxDamage - currentDamage) / maxDamage
    // Or, more simply, check if currentDamage exceeds a threshold based on maxDamage
    // We want to use a kit if current health is LESS than threshold,
    // meaning current DAMAGE is GREATER than (1 - threshold) * maxDamage.
    %damageThreshold = (1.0 - $RouteBot::KitHealthThreshold) * %maxDamage;

    // echo("Bot " @ %botObject.aiName @ " Dmg: " @ %currentDamage @ "/" @ %maxDamage @ ", DmgThresholdForKit: " @ %damageThreshold);

    if (%currentDamage > %damageThreshold) { // If damage taken is significant enough
        if (Player::getItemCount(%botObject, RepairKit) > 0) {
            echo("Bot " @ %botObject.aiName @ " is using a Repair Kit. Current Damage: " @ %currentDamage);
            Player::useItem(%botObject, RepairKit); // Server-side use item
            %botObject.lastKitUseTime = %currentTime; // Set cooldown
        } else {
            // echo("Bot " @ %botObject.aiName @ " needs kit, but has none. Damage: " @ %currentDamage);
        }
    }
}

function Server::Countdown(%time)
{
   $countdownStarted = true;
   schedule("Game::startMatch();", %time);
   Game::notifyMatchStart(%time);
   if(%time > 30)
      schedule("Game::notifyMatchStart(30);", %time - 30);
   if(%time > 15)
      schedule("Game::notifyMatchStart(15);", %time - 15);
   if(%time > 10)
      schedule("Game::notifyMatchStart(10);", %time - 10);
   if(%time > 5)
      schedule("Game::notifyMatchStart(5);", %time - 5);
}
