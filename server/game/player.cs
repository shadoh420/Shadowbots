$PlayerAnim::Crouching = 25;
$PlayerAnim::DieChest = 26;
$PlayerAnim::DieHead = 27;
$PlayerAnim::DieGrabBack = 28;
$PlayerAnim::DieRightSide = 29;
$PlayerAnim::DieLeftSide = 30;
$PlayerAnim::DieLegLeft = 31;
$PlayerAnim::DieLegRight = 32;
$PlayerAnim::DieBlownBack = 33;
$PlayerAnim::DieSpin = 34;
$PlayerAnim::DieForward = 35;
$PlayerAnim::DieForwardKneel = 36;
$PlayerAnim::DieBack = 37;

//----------------------------------------------------------------------------
$CorpseTimeoutValue = 22;
//----------------------------------------------------------------------------

// Player & Armor data block callbacks

function Player::onAdd(%this) {
	GameBase::setRechargeRate(%this,8);
}

function Player::onRemove(%this) {
	// Drop anything left at the players pos
	for (%i = 0; %i < 8; %i = %i + 1) {
		%type = Player::getMountedItem(%this,%i);
		if (%type != -1) {
			// Note: Player::dropItem is not called here.
			%item = newObject("","Item",%type,1,false);
			schedule("Item::Pop(" @ %item @ ");", $ItemPopTime, %item);

			addToSet("MissionCleanup", %item);
			GameBase::setPosition(%item,GameBase::getPosition(%this));
		}
	}
}

function Player::onNoAmmo( %player,%imageSlot,%itemType ) {
	//echo("No ammo for weapon ",%itemType.description," slot(",%imageSlot,")");
}

function Player::onKilled( %this ) {
	%cl = GameBase::getOwnerClient(%this);
	%cl.dead = 1;
	
	if ( $AutoRespawn > 0 )
		schedule("Game::autoRespawn(" @ %cl @ ");",$AutoRespawn,%cl);
	
	Player::setDamageFlash( %this, 0.75 );
	for (%i = 0; %i < 8; %i = %i + 1) {
		%type = Player::getMountedItem(%this,%i);
		if (%type != -1) {
			if (%i != $WeaponSlot || !Player::isTriggered(%this,%i) || ( getRandom() > "0.2" ) )
				Player::dropItem(%this,%type);
		}
	}

   if ( %cl != -1 ) {
		if(%this.vehicle != "")	{
			if(%this.driver != "") {
				%this.driver = "";
        	 	Client::setControlObject(Player::getClient(%this), %this);
        	 	Player::setMountObject(%this, -1, 0);
			} else {
				%this.vehicle.Seat[%this.vehicleSlot-2] = "";
				%this.vehicleSlot = "";
			}
			%this.vehicle = "";		
		}
      schedule("GameBase::startFadeOut(" @ %this @ ");", $CorpseTimeoutValue, %this);
      Client::setOwnedObject(%cl, -1);
      Client::setControlObject(%cl, Client::getObserverCamera(%cl));
      Observer::setOrbitObject(%cl, %this, 5, 5, 5);
      schedule("deleteObject(" @ %this @ ");", $CorpseTimeoutValue + 2.5, %this);
      %cl.observerMode = "dead";
      %cl.dieTime = getSimTime();
   }
}

function Player::onDamage( %this, %type, %value, %pos, %vec, %mom, %vertPos, %quadrant, %object ) {
	if ( !Player::isExposed(%this) )
		return;
	
	%damagedClient = Player::getClient(%this);
	%shooterClient = %object;
	%shooterName = Client::getName( %shooterClient );

	Player::applyImpulse(%this,%mom);
	if($teamplay && %damagedClient != %shooterClient && Client::getTeam(%damagedClient) == Client::getTeam(%shooterClient) ) {
		if (%shooterClient != -1) {
			%curTime = getSimTime();
			if ((%curTime - %this.DamageTime > 3.5 || %this.LastHarm != %shooterClient) && %damagedClient != %shooterClient && $Server::TeamDamageScale > 0) {
				if(%type != $MineDamageType) {
					Client::sendMessage(%shooterClient,0,"You just harmed Teammate " @ Client::getName(%damagedClient) @ "!");
					Client::sendMessage(%damagedClient,0,"You took Friendly Fire from " @ Client::getName(%shooterClient) @ "!");
				} else {
					Client::sendMessage(%shooterClient,0,"You just harmed Teammate " @ Client::getName(%damagedClient) @ " with your mine!");
					Client::sendMessage(%damagedClient,0,"You just stepped on Teamate " @ Client::getName(%shooterClient) @ "'s mine!");
				}
				%this.LastHarm = %shooterClient;
				%this.DamageStamp = %curTime;
			}
		}
		%friendFire = $Server::TeamDamageScale;
	} else if ( %type == $ImpactDamageType && Client::getTeam(%object.clLastMount) == Client::getTeam(%damagedClient) ) {
		%friendFire = $Server::TeamDamageScale;
	} else {
		%friendFire = 1.0;	
	}

	if (!Player::isDead(%this)) {
		%armor = Player::getArmor(%this);
		//More damage applyed to head shots
		if(%vertPos == "head" && %type == $LaserDamageType) {
			if(%armor == "harmor") { 
				if(%quadrant == "middle_back" || %quadrant == "middle_front" || %quadrant == "middle_middle") {
					%value += (%value * 0.3);
				}
			} else {
				%value += (%value * 0.3);
			}
		}

		//If Shield Pack is on
		if (%type != -1 && %this.shieldStrength) {
			%energy = GameBase::getEnergy(%this);
			%strength = %this.shieldStrength;
			if (%type == $GrenadeDamageType || %type == $MortarDamageType)
				%strength *= 0.75;
			%absorb = %energy * %strength;
			if (%value < %absorb) {
				GameBase::setEnergy(%this,%energy - ((%value / %strength)*%friendFire));
				%thisPos = getBoxCenter(%this);
				%offsetZ =((getWord(%pos,2))-(getWord(%thisPos,2)));
				GameBase::activateShield(%this,%vec,%offsetZ);
				%value = 0;
			} else {
				GameBase::setEnergy(%this,0);
				%value = %value - %absorb;
			}
		}

		if (%value) {
			%value = $DamageScale[%armor, %type] * %value * %friendFire;
			%dlevel = GameBase::getDamageLevel(%this) + %value;
			%spillOver = %dlevel - %armor.maxDamage;
			%flash = Player::getDamageFlash(%this) + %value * 2;
			if (%flash > 0.75) 
				%flash = 0.75;
			Player::setDamageFlash(%this,%flash);
			
			if ( %shooterName != "" )
				Event::Trigger( eventServerClientDamaged, %damagedClient, %shooterClient, %type, ( %spillOver >= 0 ) ? %armor.maxDamage : %value );

			if ( %dlevel >= %armor.maxDamage )
				Client::onKilled( %damagedClient, %shooterClient, %type );


    		if (isObject(%this) && %this.isRouteBot == true && !Player::isDead(%this)) {
        schedule("Rt::botCheckAndUseKit(" @ %this @ ");", 0.1);
    }
			
			// set the damage after omdamage / onkilled are called so the player object still exists
			GameBase::setDamageLevel(%this,%dlevel);

			//If player not dead then play a random hurt sound
			if(!Player::isDead(%this)) { 
				if(%damagedClient.lastDamage < getSimTime()) {
					%sound = randomItems(3,injure1,injure2,injure3);
					playVoice(%damagedClient,%sound);
					%damagedClient.lastdamage = getSimTime() + 1.5;
				}
			} else {
				if(%spillOver > 0.5 && (%type== $DiscDamageType || %type == $GrenadeDamageType || %type== $MortarDamageType|| %type == $RocketDamageType)) {
					Player::trigger(%this, $WeaponSlot, false);
					%weaponType = Player::getMountedItem(%this,$WeaponSlot);
					if(%weaponType != -1)
						Player::dropItem(%this,%weaponType);
					Player::blowUp(%this);
				} else {
					if ((%value > 0.40 && (%type== $DiscDamageType || %type == $GrenadeDamageType || %type== $MortarDamageType || %type == $RocketDamageType )) || (Player::getLastContactCount(%this) > 6) ) {
						if(%quadrant == "front_left" || %quadrant == "front_right") 
							%curDie = $PlayerAnim::DieBlownBack;
						else
							%curDie = $PlayerAnim::DieForward;
					} else if( Player::isCrouching(%this) ) {
						%curDie = $PlayerAnim::Crouching;	
					} else if(%vertPos=="head") {
						if(%quadrant == "front_left" ||	%quadrant == "front_right"	) 
							%curDie = randomItems(2, $PlayerAnim::DieHead, $PlayerAnim::DieBack);
						else 
							%curDie = randomItems(2, $PlayerAnim::DieHead, $PlayerAnim::DieForward);
					} else if (%vertPos == "torso") {
						if(%quadrant == "front_left" ) 
							%curDie = randomItems(3, $PlayerAnim::DieLeftSide, $PlayerAnim::DieChest, $PlayerAnim::DieForwardKneel);
						else if(%quadrant == "front_right") 
							%curDie = randomItems(3, $PlayerAnim::DieChest, $PlayerAnim::DieRightSide, $PlayerAnim::DieSpin);
						else if(%quadrant == "back_left" ) 
							%curDie = randomItems(4, $PlayerAnim::DieLeftSide, $PlayerAnim::DieGrabBack, $PlayerAnim::DieForward, $PlayerAnim::DieForwardKneel);
						else if(%quadrant == "back_right") 
							%curDie = randomItems(4, $PlayerAnim::DieGrabBack, $PlayerAnim::DieRightSide, $PlayerAnim::DieForward, $PlayerAnim::DieForwardKneel);
					} else if (%vertPos == "legs") {
						if(%quadrant == "front_left" ||	%quadrant == "back_left") 
							%curDie = $PlayerAnim::DieLegLeft;
						if(%quadrant == "front_right" ||	%quadrant == "back_right") 
							%curDie = $PlayerAnim::DieLegRight;
					}
					Player::setAnimation(%this, %curDie);
				}

				if(%type == $ImpactDamageType && %object.clLastMount != "")  
					%shooterClient = %object.clLastMount;
			}
		}
	}
}

function randomItems(%num, %an0, %an1, %an2, %an3, %an4, %an5, %an6) {
	return %an[floor(getRandom() * (%num - 0.01))];
}

function Player::onCollision( %this, %object ) {
	if ( !Player::isDead(%this) || ( getObjectType(%object) != "Player" ) )
		return;

	// Transfer all our items to the player
	%sound = false;
	%max = getNumItems();
	for (%i = 0; %i < %max; %i = %i + 1) {
		%count = Player::getItemCount(%this,%i);
		if (%count) {
			%delta = Item::giveItem(%object,getItemData(%i),%count);
			if (%delta > 0) {
				Player::decItemCount(%this,%i,%delta);
				%sound = true;
			}
		}
	}
	if (%sound) {
		// Play pickup if we gave him anything
		playSound(SoundPickupItem,GameBase::getPosition(%this));
	}
}

function Player::getHeatFactor(%this) {
	// Hack to avoid turret turret not tracking vehicles.
	// Assumes that if we are not in the player we are
	// controlling a vechicle, which is not always correct
	// but should be OK for now.
	%client = Player::getClient(%this);
	if (Client::getControlObject(%client) != %this)
		return 1.0;

   %time = getIntegerTime(true) >> 5;
   %lastTime = Player::lastJetTime(%this) >> 10;

   if ((%lastTime + 1.5) < %time) {
      return 0.0;
   } else {
      %diff = %time - %lastTime;
      %heat = 1.0 - (%diff / 1.5);
      return %heat;
   }
}

function Player::jump( %this,%mom ) {
   %cl = GameBase::getControlClient(%this);
   if(%cl != -1)
   {
      %vehicle = Player::getMountObject (%this);
		%this.lastMount = %vehicle;
		%this.newMountTime = getSimTime() + 3.0;
		Player::setMountObject(%this, %vehicle, 0);
		Player::setMountObject(%this, -1, 0);
		Player::applyImpulse(%pl,%mom);
		playSound (GameBase::getDataName(%this).dismountSound, GameBase::getPosition(%this));
   }
}


//----------------------------------------------------------------------------

function remoteKill(%client) {
   if(!$matchStarted)
      return;

   %player = Client::getOwnedObject(%client);
   if(%player != -1 && getObjectType(%player) == "Player" && !Player::isDead(%player)) {
		Client::onKilled(%client,%client); // player::kill after this so we still exist for triggered events
		playNextAnim(%client);
		Player::kill(%client);
   }
}

$animNumber = 25;
function playNextAnim( %client ) {
	if($animNumber > 36) 
		$animNumber = 25;		
	Player::setAnimation( %client, $animNumber++ );
}

function Player::enterMissionArea(%this)
{
   %set = nameToID("MissionCleanup/ObjectivesSet");
	%this.outArea = "";
   for(%i = 0; (%obj = Group::getObject(%set, %i)) != -1; %i++)
      GameBase::virtual(%obj, "playerEnterMissionArea", %this);
}

function Player::leaveMissionArea(%this)
{
	%this.outArea=1;
	Client::sendMessage(Player::getClient(%this),1,"You have left the mission area.");
	alertPlayer(%this, 3);
}
   
function alertPlayer( %player, %count ) {
	if ( %player.outArea != 1 )
		return;

	%cl = Player::getClient( %player );
	if ( ( %cl != -1 ) && ( %count > 0 ) ) {
		Client::sendMessage( %cl, 0, "~wLeftMissionArea.wav" );
		schedule("alertPlayer(" @ %player @ ", " @ %count - 1 @ ");",1.5,%player);
	} else {
		%set = nameToID("MissionCleanup/ObjectivesSet");
		for(%i = 0; (%obj = Group::getObject(%set, %i)) != -1; %i++)
			GameBase::virtual(%obj, "playerLeaveMissionArea", %player);
	}
}
